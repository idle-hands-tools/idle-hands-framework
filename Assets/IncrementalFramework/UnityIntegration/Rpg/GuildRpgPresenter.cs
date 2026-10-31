using IncrementalFramework.UI;
using IncrementalFramework.Unity.UI;
using IncrementalFramework.Unity;
using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Games.Rpg;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using EconomicAmount = IncrementalFramework.Economy.ResourceAmount;

namespace IncrementalFramework.Unity.Rpg
{
    public interface IGuildRpgController
    {
        GuildGame Game { get; }
        bool CatchingUp { get; }
        string Status { get; }
        void Act(Func<bool> action);
        void Save();
        void Load();
    }

    /// <summary>Runtime-built guild dashboard. Rebuilds on actions; combat only updates existing controls.</summary>
    public sealed class GuildRpgPresenter
    {
        private readonly IGuildRpgController owner;
        private readonly SampleTutorialSession tutorial;
        private GuildGame Game => owner.Game;
        private readonly Font font;
        private readonly RectTransform content;
        private readonly ScrollRect scroll;
        private readonly Text bank, materials, status;
        private readonly List<MountedUi> persistentViews = new List<MountedUi>();
        private readonly List<MountedUi> tabViews = new List<MountedUi>();
        private UiContext uiContext;
        private bool buildingTab;
        private readonly Color ink = new Color(0.89f, 0.92f, 0.94f);
        private readonly Color muted = new Color(0.61f, 0.70f, 0.74f);
        private readonly Color gold = new Color(0.94f, 0.71f, 0.39f);
        private readonly Color panel = new Color(0.10f, 0.16f, 0.20f);
        private string tab = "ROSTER";
        private int selectedParty;
        private int selectedHero;

        public GuildRpgPresenter(Transform root, IGuildRpgController owner,SampleTutorialSession tutorial=null)
        {
            this.owner = owner;this.tutorial=tutorial;
            uiContext = NewContext();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = new GameObject("Ember Guild dashboard", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(root, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 900);
            scaler.matchWidthOrHeight = 0.5f;
            var background = Element("Background", canvas.transform, typeof(Image));
            Stretch(background); background.GetComponent<Image>().color = new Color(0.045f, 0.075f, 0.10f);
            var header = Element("Header", background);
            Place(header, 22, 14, 1236, 138);
            Label(header, "EMBER GUILD", 32, 44, gold);
            Label(header, "ADVENTURERS · EXPEDITIONS · CRAFT · CONSTRUCT", 13, 24, muted);
            bank = Dynamic(header, () => string.Join("     ", Enumerable.Range(0, 4).Select(i => Game.Definition.ResourceNames[i] + "  " + F(Game.Balance(Game.Definition.ResourceIds[i]))
                + "  (+" + F(Game.Economy.GetResource(Game.Definition.ResourceIds[i]).Rate) + "/s)")), 20, 32, ink);
            materials = Dynamic(header, () => string.Join("     ", Enumerable.Range(4, 4).Select(i => Game.Definition.ResourceNames[i] + ": " + F(Game.Balance(Game.Definition.ResourceIds[i])))), 15, 28, muted);
            Vertical(header, 2);
            var nav = Element("Navigation", background);
            Place(nav, 20, 164, 174, 620);
            Vertical(nav, 10);
            foreach (string name in new[] { "ROSTER", "PARTIES", "QUESTS", "WORKSHOP", "GUILD" })
            {
                string captured = name;
                Button(nav, () => captured, () => { tab = captured; Rebuild(false); }, null, 55,"nav:"+captured);
            }
            Label(nav, "GUILD RECORDS", 13, 32, gold);
            Button(nav, () => "Save guild", owner.Save, null, 42);
            Button(nav, () => "Load guild", owner.Load, null, 42);
            Label(nav, "Combat runs every 2s.\nOffline progress: 8h.\n\nScroll inside the main panel for all actions.", 14, 140, muted);
            var viewport = Element("Viewport", background, typeof(Image), typeof(ScrollRect), typeof(RectMask2D));
            viewport.anchorMin = new Vector2(0, 0); viewport.anchorMax = new Vector2(1, 1);
            viewport.offsetMin = new Vector2(212, 88); viewport.offsetMax = new Vector2(-20, -162);
            viewport.GetComponent<Image>().color = new Color(0.065f, 0.105f, 0.14f);
            content = Element("Content", viewport, typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1); content.sizeDelta = Vector2.zero;
            Vertical(content, 12, 18);
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = viewport.GetComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.content = content; scroll.horizontal = false;
            scroll.scrollSensitivity = 35; scroll.movementType = ScrollRect.MovementType.Clamped;
            var footer = Element("Status", background);
            footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1, 0);
            footer.pivot = Vector2.zero; footer.offsetMin = new Vector2(22, 12); footer.offsetMax = new Vector2(-20, 78);
            status = Dynamic(footer, () => owner.Status, 16, 64, gold, true); Stretch((RectTransform)status.rectTransform.parent.parent);
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
            {
                var events = new GameObject("Guild input", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(root, false);
            }
            Rebuild(false);
        }
        private UiContext NewContext() => new UiContext(Game.World, Game.State, () => Game.Time,
            diagnostic: message => Debug.LogWarning(message),presentation:tutorial);
        public void Refresh()
        {
            // Explicit host refresh covers presentation-only state (selection/status/catch-up).
            foreach(var view in persistentViews.ToArray())view.Refresh();
            foreach(var view in tabViews.ToArray())view.Refresh();
        }
        public void Dispose()
        { foreach(var view in tabViews)view.Dispose();tabViews.Clear();foreach(var view in persistentViews)view.Dispose();persistentViews.Clear(); }
        public void Rebuild() { Rebuild(true); }
        private void Rebuild(bool retainScroll)
        {
            float position = retainScroll ? scroll.verticalNormalizedPosition : 1;
            foreach(var view in tabViews)view.Dispose();
            tabViews.Clear();
            if(!ReferenceEquals(uiContext.World,Game.World))
            {
                uiContext=NewContext();
                // Persistent declarations are remounted against the replacement state.
                for(int i=0;i<persistentViews.Count;i++)
                {
                    var old=persistentViews[i];var parent=old.Root.parent;int order=old.Root.GetSiblingIndex();
                    var definition=old.Element.Definition;old.Dispose();
                    var replacement=UnityUiFactory.Mount(parent,definition,uiContext);replacement.Root.SetSiblingIndex(order);persistentViews[i]=replacement;
                    // Header/status references are presentation conveniences only; their views own the text.
                }
            }
            buildingTab=true;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i).gameObject;
                child.SetActive(false); UnityEngine.Object.Destroy(child);
            }
            selectedParty = Mathf.Clamp(selectedParty, 0, Game.PartyCount - 1);
            if (!Game.State.Heroes.Any(x => x.Number == selectedHero)) selectedHero = Game.State.Heroes.FirstOrDefault()?.Number ?? 0;
            if(tutorial!=null)
            {
                Dynamic(content,()=>tutorial.Caption,16,76,gold,true);
                if(!tutorial.Completed)Button(content,()=>"Skip tutorial",()=>{tutorial.Skip();Rebuild();},null,36);
            }
            Heading(tab);
            switch (tab)
            {
                case "ROSTER": Roster(); break;
                case "PARTIES": Parties(); break;
                case "QUESTS": Quests(); break;
                case "WORKSHOP": Workshop(); break;
                case "GUILD": Guild(); break;
            }
            buildingTab=false;
            Refresh();
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = position;
        }
        private void Heading(string text) { Label(content, text, 25, 42, gold); }
        private void Description(string text, float height = 52) { Label(content, text, 16, height, muted); }
        private void ChooseParty()
        {
            var row = Row(content);
            for (int i = 0; i < Game.PartyCount; i++)
            {
                int index = i;
                Button(row, () => (selectedParty == index ? "> " : "") + Game.State.Parties[index].Name,
                    () => { selectedParty = index; Rebuild(); }, null, 42);
            }
        }
        private void Roster()
        {
            Description("Hire with gold. Select a destination party below, then assign adventurers. Four slots per party; a Guardian + damage dealer + Mage + Cleric is a good starting team.", 62);
            Dynamic(content, () => "Roster " + Game.State.Heroes.Count + "/" + Game.HeroCapacity + "  |  Parties " + Game.PartyCount + "/3  |  Upgrade the guild hall to expand.", 16, 34, ink);
            for (int i = 0; i < Game.Definition.Classes.Length; i += 2)
            {
                var row = Row(content);
                for (int j = i; j < i + 2; j++)
                {
                    var heroClass = Game.Definition.Classes[j].Id;
                    var definition = Game.Definition.Classes[j];
                    Button(row, () => "Hire " + definition.Name + " / " + definition.Role + " / " + definition.HireGold + " gold\n" + definition.Skill,
                        () => owner.Act(() => Game.Hire(heroClass)),
                        () => Game.State.Heroes.Count < Game.HeroCapacity && Game.Balance("gold") >= definition.HireGold, 82,"game-action:hire");
                }
            }
            Label(content, "ASSIGN TO PARTY", 14, 26, gold);
            ChooseParty();
            var roster=Ui.List(UiBinding<IReadOnlyList<string>>.From(_=>Game.State.Heroes.Select(h=>h.Id).ToArray()), context=>
            {
                var hero=(HeroState)context.Item;
                var train=ButtonDeclaration(()=>"Train / "+Game.TrainingCost(hero)+" gold\nRank "+hero.Training+"/"+Game.State.Buildings[2]*5,
                    ()=>owner.Act(()=>Game.Train(hero.Number)),()=>!Game.IsBusy(hero)&&hero.Training<Game.State.Buildings[2]*5&&Game.Balance("gold")>=Game.TrainingCost(hero),58);
                var assign=ButtonDeclaration(()=>"Assign to "+Game.State.Parties[selectedParty].Name,
                    ()=>owner.Act(()=>Game.Assign(hero.Number,selectedParty)),()=>!Game.IsBusy(hero)&&!Game.State.Parties[selectedParty].Active&&hero.Party!=selectedParty&&Game.Members(selectedParty).Count<4,58);
                assign.TargetId="game-action:assign";
                var rest=ButtonDeclaration(()=>"Rest at guild",()=>owner.Act(()=>Game.Assign(hero.Number,-1)),()=>!Game.IsBusy(hero)&&hero.Party>=0,58);
                var gear=ButtonDeclaration(()=>"Equipment",()=>{selectedHero=hero.Number;tab="WORKSHOP";Rebuild(false);},null,58);
                var actions=Ui.Row(train,assign,rest,gear);actions.Spacing=8;
                var summary=Ui.Text(TextTemplate.Read(_=>HeroSummary(hero)));summary.Id="summary";summary.Style.FontSize=18;summary.Style.Color=UiColorOf(ink);summary.Layout.MinHeight=76;
                var card=Ui.Panel(summary,actions);card.Padding=new UiInsets(14);card.Spacing=8;card.Style.Background=UiColorOf(panel);return card;
            });
            roster.Id="roster";roster.Spacing=12;roster.Layout.Width=UiLength.Fill();
            var empty=Ui.Text("Your guild is empty. The contract office produces gold so you can always hire more adventurers.");empty.Style.Color=UiColorOf(muted);empty.Layout.MinHeight=52;
            roster.EmptyContent=empty;Mount(content,roster);
        }
        private string HeroSummary(HeroState hero)
        {
            var cls = Game.Definition.Class(hero.Class);
            return hero.Name + " / " + cls.Name + " / " + cls.Role + " / Lv " + hero.Level + " (XP " + hero.Experience + ")"
                + "\nHP " + Math.Round(Game.MaxHealth(hero)) + "   ATK " + Math.Round(Game.Attack(hero)) + "   DEF " + Math.Round(Game.Defense(hero))
                + "   |   " + (hero.Party < 0 ? "At guild" : Game.State.Parties[hero.Party].Name) + (Game.IsBusy(hero) ? " / on quest" : " / available");
        }
        private void Parties()
        {
            Description("Party members are managed in Roster. Active parties cannot change members, gear, or training. Repeat quests automatically to farm loot and experience. A defeat stops repeating; no one dies permanently.", 66);
            for (int i = 0; i < Game.PartyCount; i++)
            {
                int index = i;
                var party = Game.State.Parties[i];
                var card = Card();
                Dynamic(card, () => party.Name.ToUpperInvariant() + " / " + Game.Members(index).Count + "/4  |  " + party.Victories + " clears, " + party.Defeats + " defeats", 21, 38, gold);
                var members = Game.Members(i);
                if (members.Count == 0) Label(card, "No adventurers assigned. Open Roster to form this party.", 16, 42, muted);
                for (int j = 0; j < members.Count; j++)
                {
                    var hero = members[j];
                    Dynamic(card, () => hero.Name + " / " + Game.Definition.Class(hero.Class).Role + " / Lv " + hero.Level, 16, 28, ink);
                    Bar(card, () => party.Active ? hero.Health / Game.MaxHealth(hero) : 1,
                        () => "HP " + Math.Ceiling(party.Active ? hero.Health : Game.MaxHealth(hero)) + "/" + Math.Round(Game.MaxHealth(hero)), new Color(0.23f, 0.58f, 0.45f));
                }
                Dynamic(card, () => party.Active ? Game.Definition.Regions[party.Region].Name + (party.Boss ? " / Boss hunt" : " / Patrol")
                    + "  |  Wave " + (party.Wave + 1) + "/3  |  Round " + party.Round : Game.Time < party.RecoveryUntil ? "Recovering for " + Math.Ceiling(party.RecoveryUntil - Game.Time) + "s" : "Ready at guild", 18, 38, gold, true);
                if (party.Active)
                {
                    Dynamic(card, () => "Enemies: " + string.Join("  |  ", party.Enemies.Where(hp => hp > 0).Select(hp => Math.Ceiling(hp) + " HP")), 16, 34, ink);
                    Bar(card, () => 1 - Math.Max(0, party.NextRound - Game.Time) / GuildDefinition.RoundSeconds,
                        () => "Next combat round", new Color(0.65f, 0.39f, 0.19f), true);
                }
                Dynamic(card, () => party.Report, 15, 104, muted);
                var row = Row(card);
                Button(row, () => "Repeat: " + (party.AutoRepeat ? "ON" : "OFF"), () => owner.Act(() => Game.SetRepeat(index, !party.AutoRepeat)), null, 42);
                Button(row, () => "Recall party", () => owner.Act(() => Game.Recall(index)), () => party.Active, 42);
                Button(row, () => "Choose quest", () => { selectedParty = index; tab = "QUESTS"; Rebuild(false); }, () => !party.Active, 42);
            }
            if (Game.PartyCount < 3) Description("Upgrade the guild hall to unlock another independent party.");
        }
        private void Quests()
        {
            Description("Patrols have three waves and can be repeated for gold, XP, and crafting drops. Boss hunts end with a stronger enemy; victory opens the next region. Recommended levels assume equipment, training, and balanced roles.", 66);
            ChooseParty();
            var party = Game.State.Parties[selectedParty];
            Dynamic(content, () => Game.Members(selectedParty).Count + "/4 assigned  |  " + (party.Active ? "Quest in progress" : "At guild") + "  |  Repeat " + (party.AutoRepeat ? "ON" : "OFF"), 17, 36, ink);
            Button(content, () => "Toggle repeat quests: " + (party.AutoRepeat ? "ON" : "OFF"), () => owner.Act(() => Game.SetRepeat(selectedParty, !party.AutoRepeat)), null, 42);
            for (int i = 0; i < Game.Definition.Regions.Length; i++)
            {
                int index = i;
                var region = Game.Definition.Regions[i];
                var card = Card();
                Dynamic(card, () => region.Name.ToUpperInvariant() + (index > Game.State.UnlockedRegion ? " / LOCKED" : " / OPEN") + (Game.State.BossesDefeated[index] ? " / BOSS DEFEATED" : ""), 22, 38, gold);
                Label(card, "Recommended Lv " + region.RecommendedLevel + "+  |  " + region.Enemy + ": " + region.Health + " HP, " + region.Attack + " attack\nDrops: " + region.MaterialName + " (each defeated enemy) + gold + party experience", 16, 60, muted);
                var row = Row(card);
                Func<bool> available = () => !party.Active && Game.Time >= party.RecoveryUntil && index <= Game.State.UnlockedRegion && Game.Members(selectedParty).Count > 0;
                Button(row, () => "Dispatch patrol\nFarm materials", () => Dispatch(index, false), available, 58,"game-action:quest");
                Button(row, () => "Hunt " + region.Boss + "\n" + (index < 3 ? "Unlock next region" : "Complete guild campaign"), () => Dispatch(index, true), available, 58);
            }
        }
        private void Dispatch(int region, bool boss)
        {
            owner.Act(() => Game.Dispatch(selectedParty, region, boss));
            if (Game.State.Parties[selectedParty].Active) { tab = "PARTIES"; Rebuild(false); }
        }
        private void Workshop()
        {
            Description("Buy iron equipment with gold. Reforge the equipped item into a higher tier using enemy drops and raw materials. Each craft consumes the old item and equips its replacement. Adventurers must be home.", 66);
            if (Game.State.Heroes.Count == 0) { Description("Hire an adventurer in Roster first."); return; }
            foreach (var hero in Game.State.Heroes)
            {
                var captured = hero;
                Button(content, () => (selectedHero == captured.Number ? "> " : "") + captured.Name + " / " + Game.Definition.Class(captured.Class).Name,
                    () => { selectedHero = captured.Number; Rebuild(); }, null, 36);
            }
            var chosen = Game.State.Heroes.First(x => x.Number == selectedHero);
            Dynamic(content, () => HeroSummary(chosen), 18, 78, ink);
            for (int i = 0; i < 3; i++)
            {
                var slot = (GearSlot)i;
                var card = Card();
                Dynamic(card, () => slot.ToString().ToUpperInvariant() + " / " + Game.Definition.GearName(slot, chosen.Gear(slot)), 20, 34, gold);
                Label(card, slot == GearSlot.Weapon ? "+9 attack per tier. Clerics also gain +3 healing per tier."
                    : slot == GearSlot.Armor ? "+28 health and +2 defense per tier." : "+12 health and +9 cleric healing per tier.", 16, 40, muted);
                int tier = chosen.Gear(slot);
                if (tier == 0)
                    Button(card, () => "Buy and equip Iron " + slot + " / " + Game.Definition.GearPrice(slot) + " gold", () => owner.Act(() => Game.BuyEquipment(chosen.Number, slot)),
                        () => !Game.IsBusy(chosen) && Game.Balance("gold") >= Game.Definition.GearPrice(slot), 46);
                else if (tier < Game.Definition.TierNames.Length - 1)
                {
                    var cost = Game.Definition.CraftCost(slot, tier + 1);
                    Label(card, "Next: " + Game.Definition.GearName(slot, tier + 1) + " / requires Workshop Lv " + tier + "\nConsumes this item + " + Costs(cost), 16, 68, muted);
                    Button(card, () => "Reforge into " + Game.Definition.GearName(slot, tier + 1), () => owner.Act(() => Game.Craft(chosen.Number, slot)),
                        () => !Game.IsBusy(chosen) && Game.State.Buildings[1] >= tier && Game.CanAfford(cost), 46);
                }
                else Label(card, "Maximum tier. Forged from the Ember dragon's realm.", 16, 42, ink);
            }
            Description("Recipe path: Iron → Briarforged (Briarwood) → Hydrascale (Marsh) → Frostbound (Frostpeak) → Emberforged (Rift).", 60);
        }
        private void Guild()
        {
            Description("Production buildings create raw materials automatically, including offline. Spend those materials to expand the guild, unlock parties, improve the workshop, or add production levels. All production buildings cap at Lv 20.", 66);
            for (int i = 0; i < 4; i++)
            {
                int index = i; string id = Game.Definition.ProductionIds[i];
                var card = Card();
                Dynamic(card, () => Game.Definition.ProductionNames[index].ToUpperInvariant() + " / Lv " + Game.Economy.GetProducer(id).Owned, 21, 36, gold);
                string resource = Game.Definition.ResourceNames[index];
                double rate = index == 0 ? 0.75 : index == 1 ? 0.6 : index == 2 ? 0.4 : 0.3;
                Label(card, "+" + rate + " " + resource.ToLowerInvariant() + "/sec per level. " + (index == 0 ? "Funds recruiting, equipment, and training." : "Supplies construction, upgrades, and crafting."), 16, 52, muted);
                Button(card, () => "Build / upgrade: " + Costs(Game.Economy.GetProducerCost(id)), () => owner.Act(() => Game.UpgradeProduction(index)),
                    () => Game.Economy.GetProducer(id).Owned < 20 && Game.CanAfford(Game.Economy.GetProducerCost(id)), 52);
            }
            for (int i = 0; i < 3; i++)
            {
                int kind = i; int maximum = i == 0 ? 3 : 4;
                var card = Card();
                Dynamic(card, () => Game.Definition.BuildingNames[kind].ToUpperInvariant() + " / Lv " + Game.State.Buildings[kind] + "/" + maximum, 21, 36, gold);
                Label(card, i == 0 ? "Each level unlocks one independent party and four roster slots (up to 16 adventurers)."
                    : i == 1 ? "Unlocks the next crafting tier. Lv 1: Briarforged; Lv 2: Hydrascale; Lv 3: Frostbound; Lv 4: Emberforged."
                    : "Each level permits five additional paid training ranks per adventurer. Each rank adds 12% base stats.", 16, 60, muted);
                Button(card, () => Game.State.Buildings[kind] >= maximum ? "Fully upgraded" : "Upgrade: " + Costs(Game.Definition.BuildingCost(kind, Game.State.Buildings[kind])),
                    () => owner.Act(() => Game.UpgradeBuilding(kind)),
                    () => Game.State.Buildings[kind] < maximum && Game.CanAfford(Game.Definition.BuildingCost(kind, Game.State.Buildings[kind])), 52);
            }
            Label(content, "GUILD JOURNAL", 21, 38, gold);
            Dynamic(content, () => string.Join("\n\n", Game.State.Log.Take(10)), 15, 380, muted);
        }

        private static UiColor UiColorOf(Color c) => new UiColor(c.r,c.g,c.b,c.a);
        private MountedUi Mount(Transform parent,UiDefinition definition)
        {
            var view=UnityUiFactory.Mount(parent,definition,uiContext);
            (buildingTab?tabViews:persistentViews).Add(view);return view;
        }
        private RectTransform Card()
        {
            var definition=Ui.Panel();definition.Spacing=8;definition.Padding=new UiInsets(14);
            definition.Style.Background=UiColorOf(panel);
            var view=Mount(content,definition);return ((IUnityUiElement)view.Element).View.Content;
        }
        private RectTransform Row(Transform parent)
        {
            var definition=Ui.Row();definition.Spacing=8;
            var view=Mount(parent,definition);return ((IUnityUiElement)view.Element).View.Content;
        }
        private Text Dynamic(Transform parent,Func<string> value,int size,float height,Color color,bool timed=false)
        { return TextView(parent,TextTemplate.Read(_=>value(),timed?.1:0),size,height,color); }
        private Text Label(Transform parent,string text,int size,float height,Color color)
        { return TextView(parent,TextTemplate.Literal(text),size,height,color); }
        private Text TextView(Transform parent,TextTemplate text,int size,float height,Color color)
        {
            var definition=Ui.Text(text);definition.Style.FontSize=size;definition.Style.Color=UiColorOf(color);
            definition.Layout.MinHeight=height;definition.Layout.Width=UiLength.Fill();
            var view=Mount(parent,definition);return ((IUnityUiElement)view.Element).View.Label;
        }
        private void Button(Transform parent,Func<string> title,Action action,Func<bool> available,float height,string targetId=null)
        { Mount(parent,ButtonDeclaration(title,action,available,height,targetId)); }
        private ButtonDefinition ButtonDeclaration(Func<string> title,Action action,Func<bool> available,float height,string targetId=null)
        {
            var definition=Ui.Button(TextTemplate.Read(_=>title(),.1),UiAction.Callback(action));
            definition.TargetId=targetId;
            definition.CanExecute=UiBinding<bool>.From(_=>!owner.CatchingUp&&(available==null||available()),.1);
            definition.Style.Background=new UiColor(.16f,.29f,.33f);definition.Style.FontSize=15;definition.Style.Color=UiColorOf(ink);
            definition.Layout.MinHeight=height;definition.Layout.Width=UiLength.Fill();
            return definition;
        }
        private void Bar(Transform parent,Func<double> fraction,Func<string> caption,Color color,bool timed=false)
        {
            var definition=Ui.Progress(UiBinding<double>.From(_=>fraction(),timed?.1:0),TextTemplate.Read(_=>caption()));
            definition.FillColor=UiColorOf(color);definition.Style.Color=UiColorOf(ink);definition.Style.FontSize=13;definition.Style.TextAlignment=UiAlignment.Center;
            definition.Layout.MinHeight=24;definition.Layout.Width=UiLength.Fill();Mount(parent,definition);
        }
        private static string F(BigDouble amount) => amount.ToString("G4");
        private static string Costs(IEnumerable<EconomicAmount> costs) => string.Join(", ", costs.Where(x => x.Amount > 0).Select(x => F(x.Amount) + " " + x.ResourceId));
        private static RectTransform Element(string name, Transform parent, params Type[] components)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            foreach (var component in components) gameObject.AddComponent(component);
            return gameObject.GetComponent<RectTransform>();
        }
        private static void Vertical(RectTransform rect, int spacing, int padding = 0)
        {
            var layout = rect.GetComponent<VerticalLayoutGroup>() ?? rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing; layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childControlHeight = true; layout.childControlWidth = true;
            layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
        }
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        { rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    }
}
