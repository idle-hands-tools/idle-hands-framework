using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Features;
using IncrementalFramework.Features.Narrative;
using IncrementalFramework.Samples.Expanded;
using IncrementalFramework.UI;
using IncrementalFramework.Unity.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IncrementalFramework.Unity.Expanded
{
    /// <summary>Shared accessible shell with mechanic-specific views; all game actions go through commands.</summary>
    public sealed class ExpandedPresenter : IDisposable
    {
        private readonly ExpandedBootstrap owner;
        private readonly RectTransform body;
        private readonly ScrollRect scroll;
        private readonly Text heading, status;
        private readonly InputField search;
        private readonly Font font;
        private MountedUi mounted;
        private readonly GameObject canvas;
        private string tab = "PLAY", filter = "";
        private int textSize = 20, quantity = 10;
        private bool highContrast;
        private float readingScroll = 1;
        private readonly Dictionary<string, List<RouteStop>> drafts = new Dictionary<string, List<RouteStop>>();
        private readonly Dictionary<string, double> limits = new Dictionary<string, double>();
        private readonly Color ink = new Color(.91f,.94f,.94f);
        private readonly Color accent = new Color(.95f,.75f,.42f);
        private readonly List<UiDefinition> items = new List<UiDefinition>();
        private FeatureGame Game => owner.Game;
        public ExpandedPresenter(Transform root, ExpandedBootstrap owner)
        {
            this.owner = owner; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvas = new GameObject("Expanded sample dashboard", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(root, false); canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 900); scaler.matchWidthOrHeight = .5f;
            var background = Rect("Background", canvas.transform); Stretch(background); background.gameObject.AddComponent<Image>().color = new Color(.035f,.065f,.085f);
            heading = NativeText(background, "Title", 30, accent); Place(heading.rectTransform, new Vector2(0,1), new Vector2(1,1), new Vector2(26,-74), new Vector2(-26,-16));
            var nav = Rect("Navigation", background); Place(nav, new Vector2(0,1), new Vector2(1,1), new Vector2(26,-126), new Vector2(-26,-82));
            nav.gameObject.AddComponent<HorizontalLayoutGroup>().spacing = 10;
            foreach (string name in new[] { "PLAY", "JOURNAL", "SETTINGS" })
            { string value = name; NativeButton(nav, name, () => SwitchTab(value)); }
            NativeButton(nav, "Save", owner.Save); NativeButton(nav, "Load", owner.Load);
            var viewport = Rect("Reading viewport", background); Place(viewport, Vector2.zero, Vector2.one, new Vector2(26,90), new Vector2(-26,-180));
            viewport.gameObject.AddComponent<Image>().color = new Color(.065f,.105f,.135f); viewport.gameObject.AddComponent<RectMask2D>();
            scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.horizontal = false; scroll.scrollSensitivity = 40;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            body = Rect("Bound content", viewport); body.anchorMin = new Vector2(0,1); body.anchorMax = Vector2.one; body.pivot = new Vector2(.5f,1); body.sizeDelta = Vector2.zero;
            scroll.content = body;
            status = NativeText(background, "Status", 17, accent); Place(status.rectTransform, Vector2.zero, new Vector2(1,0), new Vector2(26,14), new Vector2(-26,78));
            var searchRect = Rect("Journal search", background); Place(searchRect, new Vector2(0,1), new Vector2(1,1), new Vector2(26,-170), new Vector2(-26,-134));
            searchRect.gameObject.AddComponent<Image>().color = new Color(.12f,.18f,.22f);
            search = searchRect.gameObject.AddComponent<InputField>(); var searchText = NativeText(searchRect, "Search text", 18, ink); Stretch(searchText.rectTransform);
            search.textComponent = searchText; searchText.supportRichText = false;
            var placeholder = NativeText(searchRect, "Search placeholder", 18, new Color(.6f,.7f,.75f)); Stretch(placeholder.rectTransform); placeholder.text = "Search correspondence, evidence, and character profiles…"; search.placeholder = placeholder;
            search.onValueChanged.AddListener(value => { filter = value; owner.RefreshView(); }); searchRect.gameObject.SetActive(false);
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
            { var events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); events.transform.SetParent(root, false); }
        }
        private void SwitchTab(string next)
        {
            if (tab == "PLAY") readingScroll = scroll.verticalNormalizedPosition;
            tab = next; owner.RefreshView();
        }
        public void Rebuild()
        {
            float position = scroll.verticalNormalizedPosition;
            var oldSelected = EventSystem.current?.currentSelectedGameObject?.name;
            mounted?.Dispose(); mounted = null;
            foreach (Transform child in body) { child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); }
            items.Clear();
            Live(()=>owner.Tutorial.Caption);
            if(!owner.Tutorial.Completed)Button("Skip tutorial",()=>{owner.Tutorial.Skip();owner.RefreshView();});
            int index = Array.IndexOf(ExpandedCampaigns.Ids, owner.SampleId);
            heading.text = owner.Story != null ? owner.Story.Compile().Title : index >= 0 ? ExpandedCampaigns.Titles[index] : owner.SampleId;
            search.gameObject.SetActive(tab == "JOURNAL");
            if (tab == "SETTINGS") Settings();
            else if (tab == "JOURNAL") Journal();
            else if (Game.Module is ProductionModule production) Production(production);
            else if (Game.Module is LogisticsModule logistics) Logistics(logistics);
            else if (Game.Module is EcologyModule ecology) Ecology(ecology);
            else if (Game.Module is MarketModule market) Market(market);
            else if (Game.Module is NarrativeModule narrative) Narrative(narrative);
            var column = Ui.Column(items.ToArray()); column.Spacing = 12; column.Padding = new UiInsets(20,20,16,24);
            mounted = UnityUiFactory.Mount(body, column, new UiContext(Game.Simulation.Entities, clock: () => Time.unscaledTimeAsDouble,presentation:owner.Tutorial));
            var surface = mounted.Root;
            surface.anchorMin = new Vector2(0,1); surface.anchorMax = Vector2.one; surface.pivot = new Vector2(.5f,1); surface.sizeDelta = Vector2.zero;
            var fitter = body.GetComponent<ContentSizeFitter>() ?? body.gameObject.AddComponent<ContentSizeFitter>(); fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = body.GetComponent<VerticalLayoutGroup>() ?? body.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = tab == "PLAY" && Game.Module is NarrativeModule ? readingScroll : position;
            if (oldSelected != null && EventSystem.current != null)
            {
                var replacement = body.GetComponentsInChildren<Selectable>().FirstOrDefault(s => s.gameObject.name == oldSelected && s.IsInteractable());
                if (replacement != null) EventSystem.current.SetSelectedGameObject(replacement.gameObject);
            }
        }
        private void Label(string text, bool title = false)
        {
            var label = Ui.Text(text); label.Style.FontSize = title ? textSize + 4 : textSize;
            label.Style.Color = title ? C(accent) : highContrast ? new UiColor(1,1,1) : C(ink);
            label.Style.Wrap = true; label.Layout.MinHeight = title ? 38 : 28; items.Add(label);
        }
        private void Live(Func<string> text)
        {
            var label = Ui.Text(TextTemplate.Read(_ => text(), .5)); label.Style.FontSize = textSize; label.Style.Color = C(ink); label.Style.Wrap = true; items.Add(label);
        }
        private void Button(string caption, Action action, bool enabled = true, string reason = "",string tutorialTarget=null)
        {
            var button = Ui.Button(caption, new UiAction((_, input) => { action(); return UiActionResult.Success; }));
            button.TargetId=tutorialTarget;
            button.Id = "action:" + items.Count; button.Layout.MinHeight = 48; button.Style.FontSize = textSize;
            button.Style.Color = highContrast ? new UiColor(1,1,1) : C(ink); button.CanExecute = enabled;
            button.DisabledReason = reason; button.Tooltip = reason; items.Add(button);
            if (!enabled && !string.IsNullOrEmpty(reason)) Label(reason);
        }
        private void ActButton(string caption, string action, string target = "", string arg = "", double amount = 0, bool enabled = true, string reason = "")
            => Button(caption, () => owner.Act(action, target, arg, amount), enabled, reason,"game-action:"+action);
        private static UiColor C(Color c) => new UiColor(c.r,c.g,c.b,c.a);
        private static string Goods(IDictionary<string, BigDouble> items) => string.Join(" · ", items.Select(p => p.Key + " " + p.Value.ToString("F1")));
        private static string Inventory(Inventory inventory) => Goods(inventory.Items) + "\nStorage " + inventory.Used.ToString("F0") + " + " + inventory.Reserved.ToString("F0") + " reserved / " + inventory.Capacity;
        private void Production(ProductionModule m)
        {
            Label("ORE → PLATES → COMPONENTS → LAUNCH", true);
            Label("Keep materials moving. Active batches finish before recipe changes apply. Higher priorities receive shared inputs first.");
            Live(() => Inventory(m.State.Depot));
            foreach (var machine in m.State.Machines)
            {
                string id = machine.Id;
                Label(machine.Id + " · " + machine.Kind, true);
                Live(() => { var live = m.State.Machines.First(x => x.Id == id); return (live.ActiveRecipe == null ? "Waiting: " + live.Blocked : "Running " + live.ActiveRecipe) + " · next " + live.Recipe + " · priority " + live.Priority; });
                ActButton(machine.Enabled ? "Pause after this batch" : "Enable machine", "toggle", id);
                ActButton("Raise allocation priority", "priority", id, amount: machine.Priority + 1);
                foreach (var recipe in m.Definition.Recipes.Where(r => r.Machine == machine.Kind))
                    ActButton(recipe.Id + " · " + recipe.Seconds + "s · " + Goods(recipe.Inputs) + " → " + Goods(recipe.Outputs), "recipe", id, recipe.Id,
                        enabled: recipe.RequiredSection == null || m.State.Delivered.Contains(recipe.RequiredSection), reason: "Requires delivery: " + recipe.RequiredSection);
            }
            Label("EXPAND", true);
            foreach (var kind in m.Definition.Machines)
            {
                var cost = m.Definition.MachineCost * BigDouble.Pow(1.5, m.State.Machines.Count(x => x.Kind == kind));
                ActButton("Build " + kind + " · " + cost.ToString("F1") + " plates", "buy", kind);
            }
            Label("SPACECRAFT", true);
            foreach (var section in m.Definition.Sections)
                ActButton(section.Id + " · " + Goods(m.Costs(section.Id,m.State.Launches)), "deliver", section.Id, enabled: !m.State.Delivered.Contains(section.Id), reason: "Already delivered.");
            if (m.IsWon) ActButton("Launch another blueprint (+10% speed; alternate component requirements)", "replay");
        }
        private void Logistics(LogisticsModule m)
        {
            Label("VALLEY — CROSSING — RIDGE — OBSERVATORY\n         ╲       QUARRY       ╱", true);
            Label("Build links, then create cyclic itineraries. Each stop unloads first and loads the quantities you specify. Edits take effect at the next arrival.");
            Live(() => "Railway funds: " + m.State.Cash + "\n" + string.Join("\n", m.State.Stations.Select(p => p.Key + ": " + Goods(p.Value.Items))));
            foreach (var track in m.Definition.Tracks)
                ActButton(track.A + " ↔ " + track.B + " · " + track.Seconds + "s · " + track.Cost + " funds", "track", track.Id,
                    enabled: !m.State.Tracks.Contains(track.Id), reason: "Track is open.");
            ActButton("Purchase train · 60 funds", "train", enabled: m.State.Trains.Count < 3, reason: "All three train slots filled.");
            foreach (var train in m.State.Trains)
            {
                string id = train.Id;
                Label(id, true);
                ActButton("Park after next arrival", "park", id);
                Live(() => { var live = m.State.Trains.First(t => t.Id == id); return live.Station + (live.Destination == null ? " · parked" : " → " + live.Destination) + "\n" + Inventory(live.Cargo); });
                if (!drafts.TryGetValue(id, out var draft)) drafts[id] = draft = FeatureJson.Copy(train.PendingRoute ?? train.Route);
                for (int i = 0; i < draft.Count; i++)
                {
                    int row = i; var stop = draft[i]; Label((i + 1) + ". " + stop.Station + " · load " + Goods(stop.Load));
                    Button(stop.Unload ? "Unload: yes" : "Unload: no", () => { draft[row].Unload = !draft[row].Unload; owner.RefreshView(); });
                    foreach (var cargo in new[] { "food", "timber", "tools" })
                    {
                        string captured = cargo; var amount = stop.Load.TryGetValue(cargo, out var value) ? value : BigDouble.Zero;
                        Button(cargo + " load limit: " + amount + " (cycle 0/10/20/40)", () => { var old = draft[row].Load.TryGetValue(captured, out var v) ? v.ToDouble() : 0; draft[row].Load[captured] = old >= 40 ? 0 : old == 0 ? 10 : old * 2; owner.RefreshView(); });
                    }
                }
                foreach (var station in m.Definition.Stations)
                { string captured = station.Id; Button("Append stop: " + captured, () => { draft.Add(new RouteStop { Station = captured }); owner.RefreshView(); }); }
                Button("Remove last stop", () => { if (draft.Count > 0) draft.RemoveAt(draft.Count - 1); owner.RefreshView(); });
                Button("Apply itinerary", () => owner.Act("route", id, FeatureJson.Write(draft)),tutorialTarget:"game-action:route");
            }
            Label("DELIVERY CONTRACTS", true);
            foreach (var contract in m.Contracts(m.State.Season))
                ActButton(contract.Id + " @ " + contract.Station + " · " + Goods(m.CargoCosts(contract,m.State.Season)) + " · reward " + contract.Reward, "contract", contract.Id,
                    enabled: !m.State.Completed.Contains(contract.Id), reason: "Contract completed.");
            if (m.IsWon) ActButton("Start alternate-demand contracts", "replay");
        }
        private void Ecology(EcologyModule m)
        {
            Label("REEDS → VOLES → HERONS / OTTERS", true);
            Label("Keep all species within their health bands and water between 0.40 and 0.85. Free baseline restoration prevents dead ends. Introduce predators after their food supply recovers.");
            if(m.State.FieldNotes>0) Label("Field notes "+m.State.FieldNotes+": establish reeds and voles before introducing predators. Improve habitat quality when populations settle below their health bands.");
            Live(() => "Restoration funds: " + m.State.Funds + " · stable " + m.State.StableSeconds + "/" + m.Definition.VictorySeconds + "s");
            foreach (var habitat in m.State.Habitats)
            {
                string id = habitat.Id; Label(id, true);
                Live(() => { var h = m.State.Habitats.First(x => x.Id == id); return "Water " + h.Water.ToString("F2") + " · purity " + h.Purity.ToString("F2") + " · shelter " + h.Shelter.ToString("F2") + "\n" + string.Join("\n", m.Definition.Species.Select(species => species.Id + " " + h.Populations[species.Id].ToString("F1") + " (" + (h.Delta.TryGetValue(species.Id, out var d) ? d.ToString("+0.0;-0.0;0") : "0") + "/step) · healthy " + species.HealthyMin + "–" + species.HealthyMax + (species.Food == null ? " · habitat limits growth" : " · feeds on " + species.Food))); });
                foreach (string action in new[] { "restore", "water", "filter", "shelter", "drain" }) ActButton(action + (action == "restore" ? " · free baseline" : " · 10 funds"), action, id);
                foreach (var species in m.Definition.Species)
                { ActButton("Introduce 8 " + species.Id + " · 20 funds", "introduce", id, species.Id); ActButton("Relocate 10 " + species.Id + " · 10 funds", "relocate", id, species.Id); }
                Live(() => "Recent populations (oldest → newest):\n" + string.Join("\n", m.State.Habitats.First(h => h.Id == id).History.Skip(Math.Max(0, m.State.Habitats.First(h => h.Id == id).History.Count - 5)).Select(values => string.Join(" / ", values.Select(v => v.ToString("F1"))))));
            }
            if (m.IsWon) ActButton("Record field notes and restore another starting ecosystem", "replay");
        }
        private void Market(MarketModule m)
        {
            Label("MARKETS · WAREHOUSE · CIVIC CONTRACTS", true);
            Label("Purchases raise prices; sales lower them. Totals include price impact and a 3% spread. Standing orders execute in creation order and never borrow. Civic delivery windows open over a 40-minute season; buy ahead or trade while you wait.");
            Live(() => "Cash " + m.State.Cash.ToString("F2") + "\n" + Inventory(m.State.Warehouse));
            Button("Trade quantity: " + quantity + " (cycle 1/5/10/20)", () => { quantity = quantity == 1 ? 5 : quantity == 5 ? 10 : quantity == 10 ? 20 : 1; owner.RefreshView(); });
            foreach (var market in m.Definition.Markets)
            {
                Label(market, true); if (!m.State.Access.Contains(market)) { Label("Unlock through civic contracts."); continue; }
                foreach (var commodity in m.Definition.Commodities)
                {
                    string name = commodity.Id, location = market; var q = m.State.Quotes.Single(x => x.Market == market && x.Commodity == name);
                    Live(() => { var live = m.State.Quotes.Single(x => x.Market == location && x.Commodity == name); return name + " · stock " + live.Stock + " · price " + m.Price(live).ToString("F2") + "\nRecent prices: " + string.Join(" → ", live.History.Skip(Math.Max(0, live.History.Count - 6)).Select(x => x.ToString("F2"))); });
                    // Read quotes at activation time too: displayed totals refresh with the market interval.
                    var buy = Ui.Button(TextTemplate.Read(_ => "Buy " + quantity + " " + name + " · total " + m.Quote(m.State.Quotes.Single(x => x.Market == location && x.Commodity == name), quantity, true).ToString("F2"), .5), new UiAction((_, input) => { owner.Act("buy", location, name, quantity); return UiActionResult.Success; })); buy.TargetId="game-action:buy"; buy.Layout.MinHeight = 48; buy.Style.FontSize = textSize; items.Add(buy);
                    var sell = Ui.Button(TextTemplate.Read(_ => "Sell " + quantity + " " + name + " · total " + m.Quote(m.State.Quotes.Single(x => x.Market == location && x.Commodity == name), quantity, false).ToString("F2"), .5), new UiAction((_, input) => { owner.Act("sell", location, name, quantity); return UiActionResult.Success; })); sell.TargetId="game-action:sell"; sell.Layout.MinHeight = 48; sell.Style.FontSize = textSize; items.Add(sell);
                    string key = market + ":" + name; if (!limits.ContainsKey(key)) limits[key] = Math.Round(m.Price(q), 2);
                    Button("Order limit " + limits[key].ToString("F2") + " · increase 0.50", () => { limits[key] += .5; owner.RefreshView(); });
                    Button("Decrease limit 0.50", () => { limits[key] = Math.Max(.25, limits[key] - .5); owner.RefreshView(); });
                    Button("Buy up to "+quantity+" below limit", () => owner.Act("order-buy",location,name,limits[key],quantity));
                    Button("Sell up to "+quantity+" above limit", () => owner.Act("order-sell",location,name,limits[key],quantity));
                }
            }
            Label("WAREHOUSE", true); ActButton("Expand warehouse · " + (120 + m.State.Upgrades * 80) + " funds", "upgrade", enabled: m.State.Upgrades < 2, reason: "Fully expanded.");
            foreach (var commodity in m.Definition.Commodities)
            {
                string id = commodity.Id; int allocation = m.State.Allocation.TryGetValue(id, out var cap) ? cap : (int)m.State.Warehouse.Capacity.ToDouble();
                ActButton(id + " allocation " + allocation + " · add 10", "allocate", id, amount: allocation + 10);
                ActButton("Reduce " + id + " allocation by 10", "allocate", id, amount: Math.Max(0, allocation - 10));
            }
            foreach (var order in m.State.Orders) ActButton("Cancel " + order.Id + " · " + (order.Buy ? "buy" : "sell") + " " + order.Remaining + " " + order.Commodity + " @ " + order.Limit, "cancel", order.Id);
            foreach (var contract in m.Definition.Contracts)
            {
                var captured=contract;
                Live(()=>captured.Id+" delivery: "+Math.Ceiling(Math.Max(0,m.State.SeasonStartedAt+captured.AvailableAfterSeconds-Game.Time))+"s until window");
                ActButton(contract.Id+" · "+Goods(contract.Goods)+" · reward "+contract.Reward,"contract",contract.Id,enabled:!m.State.Completed.Contains(contract.Id),reason:"Completed.");
            }
            if (m.IsWon) ActButton("Begin a new demand season", "replay");
        }
        private void Narrative(NarrativeModule m)
        {
            var view = m.View;
            Label("CHAPTER " + view.Chapter + " · " + view.Speaker, true); Label(view.Subject, true); Label(view.Body);
            foreach(var evidenceId in view.EvidenceIds)
            {
                var entry=m.Definition.Journal.Single(j=>j.Id==evidenceId);string title=m.Definition.Text[entry.TitleKey];
                Button("Inspect evidence: "+title,()=>{readingScroll=scroll.verticalNormalizedPosition;tab="JOURNAL";filter=title;search.SetTextWithoutNotify(title);owner.RefreshView();});
            }
            if (view.Receipt.Count > 0) Label("RECENT CONSEQUENCES\n" + string.Join("\n", view.Receipt));
            if (m is TimeLoopModule loop)
            {
                Live(() => "Loop " + (loop.State.Loop + 1) + " · " + Math.Ceiling(Math.Max(0, loop.State.Deadline - Game.Time)) + "s before the bell\n" + (loop.State.Investigation == null ? loop.State.Message : "Investigating " + loop.State.Investigation));
                if (loop.State.AtLoopBoundary) { ActButton("Restart loop · retain knowledge and shortcuts", "reset-loop"); return; }
                if (loop.State.Investigation != null) { Label("Your investigation is in progress."); return; }
                ActButton("Wait until the thirteenth bell", "wait-bell");
            }
            if (view.Kind == StoryNodeKind.Passage) Button("Continue", () => { readingScroll = 1; owner.Act("continue", argument: view.Token); },tutorialTarget:"game-action:story");
            foreach (var choice in view.Choices)
            {
                var captured = choice;
                Button(choice.Text + (string.IsNullOrEmpty(choice.Hint) ? "" : "\n" + choice.Hint), () => { readingScroll = 1; owner.Act("choose", captured.Id, view.Token); }, choice.Enabled, choice.DisabledReason,"game-action:story");
            }
            if (view.Kind == StoryNodeKind.Ending)
            {
                Label("Ending recorded. Explore your journal or revisit a chapter.", true);
                ActButton("Begin a fresh run", "restart");
            }
            if (!(m is TimeLoopModule))
                foreach (var checkpoint in m.State.Checkpoints.Keys)
                    ActButton("Replay chapter from " + m.Definition.Text[m.Definition.Nodes.Single(n => n.Id == checkpoint).SubjectKey], "checkpoint", checkpoint);
        }
        private bool Matches(string text) => string.IsNullOrEmpty(filter) || text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        private void Journal()
        {
            if (Game.Module is NarrativeModule narrative)
            {
                var s = narrative.State; Label("CORRESPONDENCE", true);
                foreach (var entry in s.Run.Transcript)
                {
                    string text = "Chapter " + entry.Chapter + " · " + entry.Subject + "\n" + entry.Body + (entry.Response == null ? "" : "\nYOUR REPLY: " + entry.Response);
                    if (Matches(text)) Label(text);
                }
                Label("EVIDENCE", true); foreach (var evidence in s.Run.Journal.Values) if (Matches(evidence)) Label(evidence);
                Label("CORRESPONDENTS", true);
                foreach (var character in narrative.Definition.Characters.Where(c => s.Run.Transcript.Any(t => t.Speaker == c.Id)))
                { string text = character.Name + "\n" + narrative.Definition.Text[character.ProfileKey]; if (Matches(text)) Label(text); }
                if (s.Run.Variables.TryGetValue("promises", out var promises))
                { Label("PROMISES", true); foreach (var promise in promises.Members) Label(promise + (s.Run.Variables.TryGetValue("kept", out var kept) && kept.Members.Contains(promise) ? " · kept" : " · unresolved")); }
                Label("ENDING GALLERY", true);
                foreach (var id in s.Endings) { var node = narrative.Definition.Nodes.Single(n => n.Id == id); Label(narrative.Definition.Text[node.SubjectKey]); }
            }
            else if (Game.Module is MarketModule market)
            { Label("TRADE HISTORY", true); foreach (var trade in market.State.Trades.Where(Matches)) Label(trade); }
            else { Label("CAMPAIGN RECORD", true); Live(() => Game.Module.Message + "\nSimulation time: " + Game.Time.ToString("F0") + "s"); }
        }
        private void Settings()
        {
            Label("READING AND DISPLAY", true);
            Button("Text size: " + textSize + " · increase", () => { textSize = Math.Min(32, textSize + 2); owner.RefreshView(); });
            Button("Decrease text size", () => { textSize = Math.Max(16, textSize - 2); owner.RefreshView(); });
            Button("High contrast: " + highContrast, () => { highContrast = !highContrast; owner.RefreshView(); });
            Label("Navigate controls with Tab or arrow keys; activate with Enter or Space. Narrative decisions wait for you. Simulation games allow up to eight hours of offline progress.");
        }
        public void Tick() { status.text = owner.CatchingUp ? "Finishing offline work…" : owner.Status + "\n" + Game.Module.Message; }
        public void Dispose() { mounted?.Dispose(); if (canvas != null) UnityEngine.Object.Destroy(canvas); }
        private static RectTransform Rect(string name, Transform parent)
        { var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent, false); return r; }
        private static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        private static void Place(RectTransform r, Vector2 min, Vector2 max, Vector2 low, Vector2 high)
        { r.anchorMin = min; r.anchorMax = max; r.offsetMin = low; r.offsetMax = high; }
        private Text NativeText(Transform parent, string name, int size, Color color)
        { var r = Rect(name, parent); var t = r.gameObject.AddComponent<Text>(); t.font = font; t.fontSize = size; t.color = color; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; t.raycastTarget = false; return t; }
        private void NativeButton(Transform parent, string caption, Action action)
        {
            var r = Rect(caption, parent); r.gameObject.AddComponent<Image>().color = new Color(.15f,.23f,.28f);
            var button = r.gameObject.AddComponent<UnityEngine.UI.Button>(); button.onClick.AddListener(() => action());
            var label = NativeText(r, "Caption", 18, ink); label.text = caption; label.alignment = TextAnchor.MiddleCenter; Stretch(label.rectTransform);
            var layout = r.gameObject.AddComponent<LayoutElement>(); layout.preferredWidth = 130; layout.preferredHeight = 44;
        }
    }
}
