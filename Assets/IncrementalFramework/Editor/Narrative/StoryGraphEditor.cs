using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Features;
using IncrementalFramework.Features.Narrative;
using IncrementalFramework.Samples.Expanded;
using IncrementalFramework.Unity.Expanded;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace IncrementalFramework.Editor.Narrative
{
    /// <summary>Unity-owned graph metadata; the compiled story contains no editor or presentation types.</summary>
    public sealed class StoryGraphEditor : EditorWindow
    {
        private StoryAsset asset;
        private StoryDefinition story;
        private VisualElement canvas, world;
        private ScrollView inspector, diagnostics;
        private string selected, pendingNode, pendingKind;
        private int pendingIndex;
        private float zoom = 1;
        private Vector2 pan = new Vector2(30,30);
        private FeatureGame playtest;
        private string mode = "Node";
        private Label playLabel;
        [MenuItem("Idle Hands/Narrative/Story graph")]
        public static void Open() => GetWindow<StoryGraphEditor>("Story graph");
        [MenuItem("Idle Hands/Narrative/Create Floodwall story asset")]
        public static void CreateFloodwall()
        {
            var path = EditorUtility.SaveFilePanelInProject("Create story", "Floodwall", "asset", "Choose a story asset location.");
            if (string.IsNullOrEmpty(path)) return;
            var value = CreateInstance<StoryAsset>(); value.Data = FeatureJson.Write(ExpandedCampaigns.Floodwall());
            AssetDatabase.CreateAsset(value, path); AssetDatabase.SaveAssets(); Selection.activeObject = value;
            var editor = GetWindow<StoryGraphEditor>("Story graph"); editor.Select(value);
        }
        [UnityEditor.Callbacks.OnOpenAsset]
        public static bool OnOpen(int id, int line)
        {
            if (!(EditorUtility.InstanceIDToObject(id) is StoryAsset value)) return false;
            GetWindow<StoryGraphEditor>("Story graph").Select(value); return true;
        }
        private void OnEnable() { Undo.undoRedoPerformed += Reload; }
        private void OnDisable() { Undo.undoRedoPerformed -= Reload; }
        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var toolbar = new Toolbar();
            var picker = new ObjectField("Story") { objectType = typeof(StoryAsset), value = asset }; picker.style.minWidth = 250;
            picker.RegisterValueChangedCallback(e => Select(e.newValue as StoryAsset)); toolbar.Add(picker);
            toolbar.Add(new ToolbarButton(NewStory) { text = "New" });
            toolbar.Add(new ToolbarButton(() => { AssetDatabase.SaveAssets(); Validate(); }) { text = "Save / validate" });
            toolbar.Add(new ToolbarButton(() => { var path = EditorUtility.SaveFilePanel("Export compiled story", "", "story.json", "json"); if (!string.IsNullOrEmpty(path) && Validate()) System.IO.File.WriteAllText(path, FeatureJson.Write(asset.Compile())); }) { text = "Export" });
            toolbar.Add(new ToolbarButton(StartPlaytest) { text = "Playtest / restart" }); rootVisualElement.Add(toolbar);
            var tools = new Toolbar();
            foreach (StoryNodeKind kind in Enum.GetValues(typeof(StoryNodeKind)))
            { var captured = kind; tools.Add(new ToolbarButton(() => AddNode(captured)) { text = "+ " + kind }); }
            tools.Add(new ToolbarButton(CopyNode) { text = "Copy" }); tools.Add(new ToolbarButton(PasteNode) { text = "Paste" });
            tools.Add(new ToolbarButton(() => { CopyNode(); PasteNode(); }) { text = "Duplicate" }); tools.Add(new ToolbarButton(DeleteNode) { text = "Delete" });
            var find = new ToolbarSearchField(); find.style.width = 180;
            find.RegisterValueChangedCallback(e =>
            {
                if (story == null) return;
                var match = story.Nodes.FirstOrDefault(n => n.Id.IndexOf(e.newValue, StringComparison.OrdinalIgnoreCase) >= 0 || (n.SubjectKey != null && story.Text.TryGetValue(n.SubjectKey, out var text) && text.IndexOf(e.newValue, StringComparison.OrdinalIgnoreCase) >= 0));
                if (match != null) FocusNode(match.Id);
            }); tools.Add(find); rootVisualElement.Add(tools);
            var split = new VisualElement(); split.style.flexDirection = FlexDirection.Row; split.style.flexGrow = 1; rootVisualElement.Add(split);
            canvas = new VisualElement(); canvas.style.flexGrow = 1; canvas.style.overflow = Overflow.Hidden; canvas.style.backgroundColor = new Color(.08f,.105f,.13f); split.Add(canvas);
            world = new VisualElement(); world.style.position = UnityEngine.UIElements.Position.Absolute; world.style.width = 12000; world.style.height = 12000;
            var edges = new IMGUIContainer(DrawEdges); edges.style.position = UnityEngine.UIElements.Position.Absolute; edges.style.left = 0; edges.style.top = 0; edges.style.width = 12000; edges.style.height = 12000; edges.pickingMode = PickingMode.Ignore;
            world.Add(edges); canvas.Add(world);
            canvas.RegisterCallback<WheelEvent>(e => { zoom = Mathf.Clamp(zoom * (e.delta.y > 0 ? .9f : 1.1f), .25f, 1.8f); ApplyTransform(); e.StopPropagation(); });
            Vector2 previous = Vector2.zero; bool panning = false;
            canvas.RegisterCallback<PointerDownEvent>(e => { if (e.button == 2 || e.button == 1) { panning = true; previous = e.position; canvas.CapturePointer(e.pointerId); e.StopPropagation(); } });
            canvas.RegisterCallback<PointerMoveEvent>(e => { if (!panning) return; pan += (Vector2)e.position - previous; previous = e.position; ApplyTransform(); });
            canvas.RegisterCallback<PointerUpEvent>(e => { if (!panning) return; panning = false; canvas.ReleasePointer(e.pointerId); });
            var side = new VisualElement(); side.style.width = 420; split.Add(side);
            var modes = new Toolbar(); foreach (string name in new[] { "Node", "Data", "Text", "Playtest" })
            { string captured = name; modes.Add(new ToolbarButton(() => { mode = captured; Inspect(); }) { text = name }); } side.Add(modes);
            inspector = new ScrollView(); inspector.style.flexGrow = 1; side.Add(inspector);
            diagnostics = new ScrollView(); diagnostics.style.maxHeight = 150; rootVisualElement.Add(diagnostics);
            rootVisualElement.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.target is TextField || e.target is IntegerField) return;
                if (e.ctrlKey && e.keyCode == KeyCode.C) CopyNode();
                if (e.ctrlKey && e.keyCode == KeyCode.V) PasteNode();
                if (e.keyCode == KeyCode.Delete) DeleteNode();
            });
            if (asset != null) Reload();
            else inspector.Add(new HelpBox("Select a Story asset or create one. Connect nodes by selecting an output, then 'Connect here' on a target. Middle/right drag pans; the wheel zooms.", HelpBoxMessageType.Info));
        }
        private void NewStory()
        {
            var path = EditorUtility.SaveFilePanelInProject("Create story", "NewStory", "asset", "Choose a story location.");
            if (string.IsNullOrEmpty(path)) return;
            var d = new StoryDefinition { Id = "story-" + Guid.NewGuid().ToString("N"), Title = "New story", Start = "start" };
            d.Text["start.subject"] = "A beginning"; d.Text["start.body"] = "Write your story here.";
            d.Nodes.Add(new StoryNode { Id = "start", Kind = StoryNodeKind.Ending, SubjectKey = "start.subject", TextKey = "start.body" });
            var value = CreateInstance<StoryAsset>(); value.Data = FeatureJson.Write(d); AssetDatabase.CreateAsset(value, path); Select(value);
        }
        private void Select(StoryAsset value)
        { asset = value; playtest = null; selected = null; if (rootVisualElement.childCount == 0) return; Reload(); }
        private void Reload()
        {
            if (asset == null || inspector == null) return;
            try { story = asset.Read(); DrawGraph(); Inspect(); Validate(); }
            catch (Exception e) { inspector.Clear(); inspector.Add(new HelpBox(e.Message, HelpBoxMessageType.Error)); }
        }
        private void Commit(string description, Action edit, bool redraw = false)
        {
            if (asset == null) return;
            Undo.RecordObject(asset, description); edit(); asset.Data = FeatureJson.Write(story); EditorUtility.SetDirty(asset);
            if (redraw) DrawGraph(); Validate();
        }
        private StoryNodePosition Position(string id)
        {
            var position = asset.Positions.FirstOrDefault(p => p.Id == id);
            if (position == null) return new StoryNodePosition { Id = id, Position = new Vector2(story.Nodes.FindIndex(n => n.Id == id) % 4 * 290, story.Nodes.FindIndex(n => n.Id == id) / 4 * 250) };
            return position;
        }
        private void ApplyTransform() { world.transform.position = pan; world.transform.scale = new Vector3(zoom,zoom,1); world.MarkDirtyRepaint(); }
        private void DrawGraph()
        {
            if (world == null || story == null) return;
            while (world.childCount > 1) world.RemoveAt(world.childCount - 1);
            foreach (var node in story.Nodes)
            {
                var n = node; var pos = Position(n.Id).Position;
                var box = new VisualElement { name = n.Id, userData = n.Id };
                box.style.position = PositionStyle(); box.style.left = pos.x; box.style.top = pos.y; box.style.width = 255;
                box.style.backgroundColor = playtest?.Module is NarrativeModule m && m.State.Run.Current == n.Id ? new Color(.15f,.35f,.22f) : selected == n.Id ? new Color(.24f,.28f,.39f) : new Color(.13f,.18f,.23f);
                box.style.paddingLeft = box.style.paddingRight = 10; box.style.paddingTop = box.style.paddingBottom = 8;
                var title = new Label(n.Id + "\n" + n.Kind + " · chapter " + n.Chapter); title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.whiteSpace = WhiteSpace.Normal; box.Add(title);
                var subject = new Label(n.SubjectKey != null && story.Text.TryGetValue(n.SubjectKey, out var text) ? text : "Automatic branch"); subject.style.whiteSpace = WhiteSpace.Normal; box.Add(subject);
                box.Add(new Button(() => ConnectTo(n.Id)) { text = "● Connect here / select" });
                if (n.Kind == StoryNodeKind.Passage || n.Kind == StoryNodeKind.Branch) Output(box, n, "Next / fallback", "next", 0, n.Next);
                for (int i = 0; i < n.Choices.Count; i++) Output(box, n, "Choice " + (i + 1), "choice", i, n.Choices[i].Target);
                for (int i = 0; i < n.Branches.Count; i++) Output(box, n, "Branch " + (i + 1), "branch", i, n.Branches[i].Target);
                box.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; selected = n.Id; Inspect(); });
                bool dragging = false; Vector2 origin = Vector2.zero, initial = Vector2.zero;
                title.RegisterCallback<PointerDownEvent>(e =>
                { if (e.button != 0) return; selected = n.Id; Inspect(); dragging = true; origin = e.position; initial = Position(n.Id).Position; Undo.RecordObject(asset, "Move story node"); title.CapturePointer(e.pointerId); e.StopPropagation(); });
                title.RegisterCallback<PointerMoveEvent>(e =>
                {
                    if (!dragging) return;
                    var position = initial + ((Vector2)e.position - origin) / zoom;
                    box.style.left = position.x; box.style.top = position.y;
                    var stored = asset.Positions.FirstOrDefault(p => p.Id == n.Id);
                    if (stored == null) { stored = new StoryNodePosition { Id = n.Id }; asset.Positions.Add(stored); }
                    stored.Position = position; EditorUtility.SetDirty(asset); world[0].MarkDirtyRepaint();
                });
                title.RegisterCallback<PointerUpEvent>(e => { dragging = false; title.ReleasePointer(e.pointerId); }); world.Add(box);
            }
            ApplyTransform();
        }
        private static UnityEngine.UIElements.Position PositionStyle() => UnityEngine.UIElements.Position.Absolute;
        private void Output(VisualElement box, StoryNode node, string label, string kind, int index, string target)
        {
            box.Add(new Button(() => { pendingNode = node.Id; pendingKind = kind; pendingIndex = index; selected = node.Id; Inspect(); })
            { text = label + " → " + (target ?? "(connect)") });
        }
        private void ConnectTo(string id)
        {
            if (pendingNode != null)
            {
                Commit("Connect story nodes", () =>
                {
                    var from = story.Nodes.Single(n => n.Id == pendingNode);
                    if (pendingKind == "next") from.Next = id;
                    else if (pendingKind == "choice") from.Choices[pendingIndex].Target = id;
                    else from.Branches[pendingIndex].Target = id;
                    pendingNode = null;
                }, true);
            }
            selected = id; Inspect(); DrawGraph();
        }
        private void DrawEdges()
        {
            if (story == null || asset == null) return;
            foreach (var n in story.Nodes)
            {
                var from = Position(n.Id).Position + new Vector2(255,60);
                var targets = n.Choices.Select(c => c.Target).Concat(n.Branches.Select(b => b.Target));
                if (n.Kind == StoryNodeKind.Passage || n.Kind == StoryNodeKind.Branch) targets = targets.Concat(new[] { n.Next });
                foreach (var target in targets.Where(t => t != null && story.Nodes.Any(x => x.Id == t)))
                {
                    var to = Position(target).Position + new Vector2(0,60);
                    Handles.DrawBezier(from, to, from + Vector2.right * 80, to + Vector2.left * 80, new Color(.5f,.7f,.85f), null, 2);
                }
            }
        }
        private void FocusNode(string id)
        { selected = id; pan = new Vector2(30,30) - Position(id).Position * zoom; DrawGraph(); Inspect(); }
        private void AddNode(StoryNodeKind kind)
        {
            if (story == null) return;
            Commit("Add story node", () =>
            {
                string id = "node-" + Guid.NewGuid().ToString("N").Substring(0,8);
                story.Text[id + ".subject"] = "New passage"; story.Text[id + ".body"] = "Write passage text.";
                story.Nodes.Add(new StoryNode { Id = id, Kind = kind, TextKey = id + ".body", SubjectKey = id + ".subject" }); selected = id;
            }, true); Inspect();
        }
        private void CopyNode()
        { var node = story?.Nodes.FirstOrDefault(n => n.Id == selected); if (node != null) EditorGUIUtility.systemCopyBuffer = "idle-hands-node:" + FeatureJson.Write(node); }
        private void PasteNode()
        {
            string value = EditorGUIUtility.systemCopyBuffer;
            if (story == null || !value.StartsWith("idle-hands-node:", StringComparison.Ordinal)) return;
            var node = FeatureJson.Read<StoryNode>(value.Substring(16));
            Commit("Paste story node", () =>
            {
                string old = node.Id; node.Id = "node-" + Guid.NewGuid().ToString("N").Substring(0,8);
                foreach (var choice in node.Choices) choice.Id = "choice-" + Guid.NewGuid().ToString("N");
                if (node.Next == old) node.Next = node.Id;
                foreach (var c in node.Choices.Where(c => c.Target == old)) c.Target = node.Id;
                foreach (var b in node.Branches.Where(b => b.Target == old)) b.Target = node.Id;
                // Copies own their editable passage/response text. Referenced shared keys remain shared intentionally.
                string CloneText(string key, string suffix)
                { if (string.IsNullOrEmpty(key) || !story.Text.TryGetValue(key, out var text)) return key; string fresh = node.Id + suffix; story.Text[fresh] = text; return fresh; }
                node.TextKey = CloneText(node.TextKey, ".body"); node.SubjectKey = CloneText(node.SubjectKey, ".subject");
                for (int i = 0; i < node.Choices.Count; i++) node.Choices[i].TextKey = CloneText(node.Choices[i].TextKey, ".choice" + i);
                story.Nodes.Add(node); selected = node.Id;
            }, true); Inspect();
        }
        private void DeleteNode()
        {
            if (story == null || selected == null) return;
            Commit("Delete story node", () => { story.Nodes.RemoveAll(n => n.Id == selected); asset.Positions.RemoveAll(p => p.Id == selected); selected = null; }, true); Inspect();
        }
        private bool Validate()
        {
            if (diagnostics == null || story == null) return false;
            diagnostics.Clear(); var errors = StoryCompiler.Validate(story);
            if (errors.Count == 0) diagnostics.Add(new Label("Story valid · " + story.Nodes.Count + " nodes · " + story.Nodes.Count(n => n.Kind == StoryNodeKind.Ending) + " endings"));
            foreach (var e in errors) { var error = e; diagnostics.Add(new Button(() => { if (story.Nodes.Any(n => n.Id == error.Node)) FocusNode(error.Node); }) { text = error.ToString() }); }
            return errors.Count == 0;
        }
        private void Field(VisualElement parent, string label, string value, Action<string> set, bool multiline = false)
        {
            var input = new TextField(label) { value = value ?? "", multiline = multiline, isDelayed = !multiline };
            input.RegisterValueChangedCallback(e => Commit("Edit " + label, () => set(e.newValue))); parent.Add(input);
        }
        private void Number(VisualElement parent, string label, int value, Action<int> set)
        { var input = new IntegerField(label) { value = value, isDelayed = true }; input.RegisterValueChangedCallback(e => Commit("Edit " + label, () => set(e.newValue))); parent.Add(input); }
        private void Toggle(VisualElement parent, string label, bool value, Action<bool> set)
        { var input = new Toggle(label) { value = value }; input.RegisterValueChangedCallback(e => Commit("Edit " + label, () => set(e.newValue))); parent.Add(input); }
        private void EnumInput<T>(VisualElement parent, string label, T value, Action<T> set) where T : Enum
        { var input = new EnumField(label, value); input.RegisterValueChangedCallback(e => { Commit("Edit " + label, () => set((T)e.newValue), true); Inspect(); }); parent.Add(input); }
        private void Picker(VisualElement parent, string label, string value, IEnumerable<string> options, Action<string> set)
        {
            var choices = new List<string> { "" }; choices.AddRange(options.Where(x => x != null).Distinct()); if (!choices.Contains(value ?? "")) choices.Add(value);
            var input = new PopupField<string>(label, choices, Math.Max(0, choices.IndexOf(value ?? "")));
            input.RegisterValueChangedCallback(e => Commit("Edit " + label, () => set(string.IsNullOrEmpty(e.newValue) ? null : e.newValue), true)); parent.Add(input);
        }
        private void TextEntry(VisualElement parent, string label, string key, Action<string> set)
        {
            Field(parent, label + " key", key, set);
            if (string.IsNullOrEmpty(key)) return;
            Field(parent, label + " text", story.Text.TryGetValue(key, out var text) ? text : "", value => story.Text[key] = value, true);
        }
        private void Inspect()
        {
            if (inspector == null || story == null) return; inspector.Clear();
            if (pendingNode != null) inspector.Add(new HelpBox("Connecting " + pendingNode + ". Select 'Connect here' on the destination.", HelpBoxMessageType.Info));
            if (mode == "Data") { DataInspector(); return; }
            if (mode == "Text") { foreach (var key in story.Text.Keys.ToArray()) { string captured = key; Field(inspector, key, story.Text[key], value => story.Text[captured] = value, true); } return; }
            if (mode == "Playtest") { PlayInspector(); return; }
            var n = story.Nodes.FirstOrDefault(x => x.Id == selected);
            if (n == null) { inspector.Add(new Label("Select a node. Drag its title to move it.")); return; }
            inspector.Add(new Label("Stable ID: " + n.Id));
            EnumInput(inspector, "Node kind", n.Kind, (StoryNodeKind value) => n.Kind = value);
            Number(inspector, "Chapter", n.Chapter, value => n.Chapter = Math.Max(1,value));
            Toggle(inspector, "Chapter checkpoint", n.Checkpoint, value => n.Checkpoint = value);
            Picker(inspector, "Speaker", n.Speaker, story.Characters.Select(c => c.Id), value => n.Speaker = value);
            TextEntry(inspector, "Subject", n.SubjectKey, value => n.SubjectKey = value); TextEntry(inspector, "Body", n.TextKey, value => n.TextKey = value);
            if (n.Kind == StoryNodeKind.Passage || n.Kind == StoryNodeKind.Branch) Picker(inspector, "Next / fallback", n.Next, story.Nodes.Select(x => x.Id), value => n.Next = value);
            Effects(inspector, n.Effects);
            inspector.Add(new Label("Conditional text variants (first match wins)"));
            foreach (var v in n.Variants.ToArray())
            {
                TextEntry(inspector, "Variant", v.TextKey, value => v.TextKey = value); Condition(inspector, v.Condition);
                inspector.Add(new Button(() => { Commit("Remove variant", () => n.Variants.Remove(v)); Inspect(); }) { text = "Remove variant" });
            }
            inspector.Add(new Button(() => { Commit("Add variant", () => n.Variants.Add(new StoryTextVariant { TextKey = n.TextKey })); Inspect(); }) { text = "+ Text variant" });
            if (n.Kind == StoryNodeKind.Choice)
            {
                foreach (var choice in n.Choices.ToArray())
                {
                    inspector.Add(new Label("RESPONSE " + choice.Id));
                    TextEntry(inspector, "Response", choice.TextKey, value => choice.TextKey = value);
                    Picker(inspector, "Target", choice.Target, story.Nodes.Select(x => x.Id), value => choice.Target = value);
                    EnumInput(inspector, "Unavailable", choice.Unavailable, (UnavailableChoice value) => choice.Unavailable = value);
                    TextEntry(inspector, "Locked reason", choice.DisabledReasonKey, value => choice.DisabledReasonKey = value);
                    TextEntry(inspector, "Known consequence hint", choice.HintKey, value => choice.HintKey = value);
                    Condition(inspector, choice.Requirement); Effects(inspector, choice.Effects);
                    inspector.Add(new Button(() => { Commit("Remove choice", () => n.Choices.Remove(choice), true); Inspect(); }) { text = "Remove response" });
                }
                inspector.Add(new Button(() => { Commit("Add choice", () => { string id = "choice-" + Guid.NewGuid().ToString("N"); story.Text[id] = "New response"; n.Choices.Add(new StoryChoice { Id = id, TextKey = id }); }, true); Inspect(); }) { text = "+ Response" });
            }
            if (n.Kind == StoryNodeKind.Branch)
            {
                foreach (var branch in n.Branches.ToArray())
                {
                    Condition(inspector, branch.Condition); Picker(inspector, "Branch target", branch.Target, story.Nodes.Select(x => x.Id), value => branch.Target = value);
                    inspector.Add(new Button(() => { Commit("Reorder branch", () => { int at = n.Branches.IndexOf(branch); if (at > 0) { n.Branches.RemoveAt(at); n.Branches.Insert(at - 1, branch); } }, true); Inspect(); }) { text = "Move branch earlier" });
                    inspector.Add(new Button(() => { Commit("Remove branch", () => n.Branches.Remove(branch), true); Inspect(); }) { text = "Remove branch" });
                }
                inspector.Add(new Button(() => { Commit("Add branch", () => n.Branches.Add(new StoryBranch()), true); Inspect(); }) { text = "+ Ordered branch" });
            }
            inspector.Add(new Label("Journal unlocks"));
            foreach (var entry in story.Journal)
                Toggle(inspector, entry.Id, n.Journal.Contains(entry.Id), value => { if (value) n.Journal.Add(entry.Id); else n.Journal.Remove(entry.Id); });
        }
        private void Condition(VisualElement parent, StoryCondition condition)
        {
            var group = new VisualElement(); group.style.marginLeft = 8; group.style.marginTop = 6; parent.Add(group);
            EnumInput(group, "Condition", condition.Kind, (ConditionKind value) => condition.Kind = value);
            if (condition.Kind == ConditionKind.All || condition.Kind == ConditionKind.Any || condition.Kind == ConditionKind.Not)
            {
                foreach (var child in condition.Children.ToArray())
                { Condition(group, child); group.Add(new Button(() => { Commit("Remove condition", () => condition.Children.Remove(child)); Inspect(); }) { text = "Remove operand" }); }
                group.Add(new Button(() => { Commit("Add condition", () => condition.Children.Add(new StoryCondition())); Inspect(); }) { text = "+ Operand" }); return;
            }
            if (condition.Kind == ConditionKind.Always) return;
            var kind = condition.Kind == ConditionKind.Boolean ? StoryVariableKind.Boolean : condition.Kind == ConditionKind.Contains ? StoryVariableKind.Set : StoryVariableKind.Integer;
            Picker(group, "Variable", condition.Variable, story.Variables.Where(v => v.Kind == kind).Select(v => v.Id), value => condition.Variable = value);
            if (kind == StoryVariableKind.Boolean) Toggle(group, "Equals", condition.Boolean, value => condition.Boolean = value);
            else if (kind == StoryVariableKind.Integer) Number(group, "Value", condition.Integer, value => condition.Integer = value);
            else Field(group, "Member ID", condition.Member, value => condition.Member = value);
        }
        private void Effects(VisualElement parent, List<StoryEffect> effects)
        {
            parent.Add(new Label("Effects (committed together)"));
            foreach (var e in effects.ToArray())
            {
                EnumInput(parent, "Effect", e.Kind, (StoryEffectKind value) => e.Kind = value);
                var kind = e.Kind == StoryEffectKind.SetBoolean ? StoryVariableKind.Boolean : e.Kind == StoryEffectKind.AddInteger || e.Kind == StoryEffectKind.SetInteger ? StoryVariableKind.Integer : StoryVariableKind.Set;
                Picker(parent, "Variable", e.Variable, story.Variables.Where(v => v.Kind == kind).Select(v => v.Id), value => e.Variable = value);
                if (kind == StoryVariableKind.Boolean) Toggle(parent, "Value", e.Boolean, value => e.Boolean = value);
                else if (kind == StoryVariableKind.Integer) Number(parent, "Value", e.Integer, value => e.Integer = value);
                else Field(parent, "Member ID", e.Member, value => e.Member = value);
                TextEntry(parent, "Disclosure (empty = hidden)", e.DisclosureKey, value => e.DisclosureKey = value);
                parent.Add(new Button(() => { Commit("Remove effect", () => effects.Remove(e)); Inspect(); }) { text = "Remove effect" });
            }
            parent.Add(new Button(() => { Commit("Add effect", () => effects.Add(new StoryEffect())); Inspect(); }) { text = "+ Effect" });
        }
        private void DataInspector()
        {
            Field(inspector, "Story ID", story.Id, value => story.Id = value); Field(inspector, "Title", story.Title, value => story.Title = value);
            Picker(inspector, "Start node", story.Start, story.Nodes.Select(n => n.Id), value => story.Start = value);
            inspector.Add(new Label("VARIABLES"));
            foreach (var v in story.Variables.ToArray())
            {
                Field(inspector, "Stable variable ID", v.Id, value => v.Id = value);
                EnumInput(inspector, "Type", v.Kind, (StoryVariableKind value) => v.Kind = value);
                Toggle(inspector, "Survives time loop", v.SurvivesLoop, value => v.SurvivesLoop = value);
                if (v.Kind == StoryVariableKind.Boolean) Toggle(inspector, "Initial value", v.Initial.Boolean, value => v.Initial.Boolean = value);
                else if (v.Kind == StoryVariableKind.Integer) Number(inspector, "Initial value", v.Initial.Integer, value => v.Initial.Integer = value);
                else Field(inspector, "Initial member IDs (comma separated)", string.Join(",", v.Initial.Members), value => v.Initial.Members = value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList());
                inspector.Add(new Button(() => { Commit("Delete variable", () => story.Variables.Remove(v)); Inspect(); }) { text = "Delete variable" });
            }
            inspector.Add(new Button(() => { Commit("Add variable", () => story.Variables.Add(new StoryVariable { Id = "variable-" + Guid.NewGuid().ToString("N").Substring(0,8) })); Inspect(); }) { text = "+ Variable" });
            inspector.Add(new Label("CHARACTERS"));
            foreach (var c in story.Characters.ToArray())
            {
                Field(inspector, "Character ID", c.Id, value => c.Id = value); Field(inspector, "Display name", c.Name, value => c.Name = value);
                TextEntry(inspector, "Profile", c.ProfileKey, value => c.ProfileKey = value);
                inspector.Add(new Button(() => { Commit("Delete character", () => story.Characters.Remove(c)); Inspect(); }) { text = "Delete character" });
            }
            inspector.Add(new Button(() => { Commit("Add character", () => { string id = "character-" + Guid.NewGuid().ToString("N").Substring(0,8); story.Text[id] = "Character profile"; story.Characters.Add(new StoryCharacter { Id = id, Name = "New character", ProfileKey = id }); }); Inspect(); }) { text = "+ Character" });
            inspector.Add(new Label("JOURNAL"));
            foreach (var j in story.Journal.ToArray())
            {
                Field(inspector, "Entry ID", j.Id, value => j.Id = value); TextEntry(inspector, "Title", j.TitleKey, value => j.TitleKey = value); TextEntry(inspector, "Entry", j.TextKey, value => j.TextKey = value);
                inspector.Add(new Button(() => { Commit("Delete journal entry", () => story.Journal.Remove(j)); Inspect(); }) { text = "Delete entry" });
            }
            inspector.Add(new Button(() => { Commit("Add journal entry", () => { string id = "entry-" + Guid.NewGuid().ToString("N").Substring(0,8); story.Text[id + ".title"] = "New evidence"; story.Text[id + ".body"] = "Evidence description"; story.Journal.Add(new StoryJournalEntry { Id = id, TitleKey = id + ".title", TextKey = id + ".body" }); }); Inspect(); }) { text = "+ Journal entry" });
            Field(inspector, "New text key", "", value => { if (!string.IsNullOrWhiteSpace(value) && !story.Text.ContainsKey(value)) story.Text[value] = ""; });
        }
        private void StartPlaytest()
        {
            if (!Validate()) return;
            playtest = new FeatureGame(new NarrativeModule(asset.Compile())); mode = "Playtest"; DrawGraph(); Inspect();
        }
        private void PlayInspector()
        {
            if (!(playtest?.Module is NarrativeModule m)) { inspector.Add(new Label("Save/validate, then select Playtest.")); return; }
            var view = m.View; playLabel = new Label(view.Subject + "\n\n" + view.Body); playLabel.style.whiteSpace = WhiteSpace.Normal; inspector.Add(playLabel);
            void Act(string action, string target = "", string token = "") { var result = playtest.Execute(action, target, token); if (!result.Success) Debug.LogWarning(result.Error); DrawGraph(); Inspect(); }
            if (view.Kind == StoryNodeKind.Passage) inspector.Add(new Button(() => Act("continue", token: view.Token)) { text = "Continue" });
            var node = m.Definition.Nodes.Single(n => n.Id == m.State.Run.Current);
            foreach (var choice in node.Choices)
            {
                bool eligible = NarrativeModule.Test(m.Definition, m.State.Run, choice.Requirement);
                inspector.Add(new Label(choice.Id + " · " + (eligible ? "available" : choice.Unavailable.ToString()) + "\n" + FeatureJson.Write(choice.Requirement)));
                var button = new Button(() => Act("choose", choice.Id, view.Token)) { text = m.Definition.Text[choice.TextKey] }; button.SetEnabled(eligible); inspector.Add(button);
            }
            inspector.Add(new Label("PLAYTEST VARIABLES (asset defaults are unchanged)"));
            foreach (var v in m.Definition.Variables)
            {
                var value = m.State.Run.Variables[v.Id];
                void Edit(Action<StoryValue> update)
                {
                    var envelope = FeatureJson.Read<FeatureEnvelope<NarrativeState>>(m.Capture()); update(envelope.State.Run.Variables[v.Id]);
                    m.Restore(m.Version, FeatureJson.Write(envelope)); Inspect();
                }
                if (v.Kind == StoryVariableKind.Boolean)
                { var field = new Toggle(v.Id) { value = value.Boolean }; field.RegisterValueChangedCallback(e => Edit(x => x.Boolean = e.newValue)); inspector.Add(field); }
                else if (v.Kind == StoryVariableKind.Integer)
                { var field = new IntegerField(v.Id) { value = value.Integer, isDelayed = true }; field.RegisterValueChangedCallback(e => Edit(x => x.Integer = e.newValue)); inspector.Add(field); }
                else
                { var field = new TextField(v.Id) { value = string.Join(",", value.Members), isDelayed = true }; field.RegisterValueChangedCallback(e => Edit(x => x.Members = e.newValue.Split(',').Select(y => y.Trim()).Where(y => y.Length > 0).Distinct().ToList())); inspector.Add(field); }
            }
            foreach (var checkpoint in m.State.Checkpoints.Keys) inspector.Add(new Button(() => Act("checkpoint", checkpoint)) { text = "Replay " + checkpoint });
            inspector.Add(new Label("Visits: " + string.Join(", ", m.State.Run.Visited)));
        }
    }
}
