using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;

namespace IncrementalFramework.Features.Narrative
{
    public sealed class TranscriptEntry
    {
        public string Node { get; set; }
        public string Speaker { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public int Chapter { get; set; }
        public string Response { get; set; }
        public List<string> EvidenceIds { get; set; } = new List<string>();
    }
    public sealed class StoryRun
    {
        public string Current { get; set; }
        public Dictionary<string, StoryValue> Variables { get; set; } = new Dictionary<string, StoryValue>();
        public List<TranscriptEntry> Transcript { get; set; } = new List<TranscriptEntry>();
        public Dictionary<string, string> Journal { get; set; } = new Dictionary<string, string>();
        public List<string> Visited { get; set; } = new List<string>();
        public List<string> Receipt { get; set; } = new List<string>();
    }
    public sealed class NarrativeState : FeatureState
    {
        public StoryRun Run { get; set; } = new StoryRun();
        public Dictionary<string, StoryRun> Checkpoints { get; set; } = new Dictionary<string, StoryRun>();
        public List<string> Endings { get; set; } = new List<string>();
        public long Revision { get; set; }
        public int Loop { get; set; }
        public double Deadline { get; set; }
        public bool AtLoopBoundary { get; set; }
        public string Investigation { get; set; }
        public bool AwaitingDecision { get; set; }
        public double PauseTime { get; set; }
    }
    public sealed class ChoiceView
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public string Hint { get; set; }
        public bool Enabled { get; set; }
        public string DisabledReason { get; set; }
    }
    public sealed class NarrativeView
    {
        public string Token { get; set; }
        public StoryNodeKind Kind { get; set; }
        public int Chapter { get; set; }
        public string Speaker { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
        public List<ChoiceView> Choices { get; set; } = new List<ChoiceView>();
        public List<string> Receipt { get; set; }
        public List<string> EvidenceIds { get; set; } = new List<string>();
    }

    public class NarrativeModule : FeatureModule<NarrativeState>, IncrementalFramework.Progression.Timing.ITimelineGate
    {
        public override string Id => "narrative";
        public override bool RunsOffline => false;
        public virtual bool IsTimeBlocked => true;
        public StoryDefinition Definition { get; }
        public NarrativeModule(StoryDefinition d) : this(d, StoryCompiler.Compile(d)) { }
        protected NarrativeModule(StoryDefinition d, object content) : base(Initial(StoryCompiler.Compile(d)), content)
        { Definition = StoryCompiler.Compile(d); }
        private static NarrativeState Initial(StoryDefinition d)
        {
            var s = new NarrativeState(); s.Run.Variables = d.Variables.ToDictionary(v => v.Id, v => FeatureJson.Copy(v.Initial));
            Enter(d, s, d.Start); return s;
        }
        public string Token => State.Run.Current + ":" + State.Revision;
        public NarrativeView View
        {
            get
            {
                var s = State; var n = Definition.Nodes.Single(x => x.Id == s.Run.Current);
                var entry = s.Run.Transcript.LastOrDefault();
                var view = new NarrativeView { Token = n.Id + ":" + s.Revision, Kind = n.Kind, Chapter = n.Chapter,
                    Speaker = Definition.Characters.FirstOrDefault(x => x.Id == n.Speaker)?.Name ?? Definition.Title,
                    Subject = entry?.Subject, Body = entry?.Body, Receipt = s.Run.Receipt.ToList(), EvidenceIds = entry?.EvidenceIds.ToList() ?? new List<string>() };
                foreach (var choice in n.Choices)
                {
                    bool enabled = Test(Definition, s.Run, choice.Requirement);
                    if (!enabled && choice.Unavailable == UnavailableChoice.Hidden) continue;
                    view.Choices.Add(new ChoiceView { Id = choice.Id, Text = Resolve(Definition, s.Run, choice.TextKey), Enabled = enabled,
                        Hint = Resolve(Definition, s.Run, choice.HintKey), DisabledReason = enabled ? "" : Resolve(Definition, s.Run, choice.DisabledReasonKey) });
                }
                return view;
            }
        }
        protected override string Act(NarrativeState s, FeatureCommand c, double time)
        {
            if (c.Action == "restart")
            {
                s.Run = new StoryRun { Variables = Definition.Variables.ToDictionary(v => v.Id, v => FeatureJson.Copy(v.Initial)) };
                s.Checkpoints.Clear(); s.Won = false; s.Revision++; Enter(Definition, s, Definition.Start); return null;
            }
            if (c.Action == "checkpoint")
            {
                if (!s.Checkpoints.TryGetValue(c.Target, out var run)) return "Unknown chapter checkpoint.";
                s.Run = FeatureJson.Copy(run); s.Won = Definition.Nodes.Single(n=>n.Id==s.Run.Current).Kind==StoryNodeKind.Ending; s.Revision++; return null;
            }
            if (c.Argument != s.Run.Current + ":" + s.Revision) return "This response belongs to an earlier passage. Refresh the choices.";
            var node = Definition.Nodes.Single(n => n.Id == s.Run.Current); s.Run.Receipt.Clear();
            string target;
            if (c.Action == "continue")
            {
                if (node.Kind != StoryNodeKind.Passage) return "Choose a response first.";
                target = node.Next;
            }
            else if (c.Action == "choose")
            {
                var choice = node.Choices.FirstOrDefault(x => x.Id == c.Target);
                if (node.Kind != StoryNodeKind.Choice || choice == null || !Test(Definition, s.Run, choice.Requirement)) return "Response is unavailable.";
                string shownResponse = Resolve(Definition, s.Run, choice.TextKey);
                Apply(Definition, s.Run, choice.Effects);
                s.Run.Transcript.Last().Response = shownResponse; target = choice.Target;
            }
            else return "Unknown narrative action.";
            Enter(Definition, s, target); s.Revision++; return null;
        }
        public static bool Test(StoryDefinition d, StoryRun run, StoryCondition condition)
        {
            var template = new EntityTemplate("story");
            foreach (var variable in d.Variables)
            {
                var value = run.Variables[variable.Id];
                if (variable.Kind == StoryVariableKind.Boolean) template.Define(new PropertyKey<bool>(variable.Id), value.Boolean);
                else if (variable.Kind == StoryVariableKind.Integer) template.Define(new PropertyKey<int>(variable.Id), value.Integer);
                else template.Define(new PropertyKey<string[]>(variable.Id), value.Members.ToArray());
            }
            var entity = template.Instantiate(new EntityContext(), "run");
            return condition.Compile().Evaluate(new EvaluationContext(entity));
        }
        public static string Resolve(StoryDefinition d, StoryRun run, string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var text = Regex.Replace(d.Text[key], @"\[\[([\w.-]+)\|([^\]]+)\]\]", match => match.Groups[2].Value);
            return Regex.Replace(text, @"\{([\w.-]+)\}", match =>
            {
                if (!run.Variables.TryGetValue(match.Groups[1].Value, out var value)) return match.Value;
                var variable = d.Variables.Single(v => v.Id == match.Groups[1].Value);
                return variable.Kind == StoryVariableKind.Boolean ? value.Boolean.ToString() : variable.Kind == StoryVariableKind.Integer
                    ? value.Integer.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Join(", ", value.Members);
            });
        }
        protected static void Apply(StoryDefinition d, StoryRun run, IEnumerable<StoryEffect> effects)
        {
            foreach (var effect in effects)
            {
                var value = run.Variables[effect.Variable];
                switch (effect.Kind)
                {
                    case StoryEffectKind.SetBoolean: value.Boolean = effect.Boolean; break;
                    case StoryEffectKind.SetInteger: value.Integer = effect.Integer; break;
                    case StoryEffectKind.AddInteger: value.Integer = checked(value.Integer + effect.Integer); break;
                    case StoryEffectKind.Include: if (!value.Members.Contains(effect.Member)) value.Members.Add(effect.Member); break;
                    case StoryEffectKind.Remove: value.Members.Remove(effect.Member); break;
                }
                if (!string.IsNullOrEmpty(effect.DisclosureKey)) run.Receipt.Add(Resolve(d, run, effect.DisclosureKey));
            }
        }
        protected static void Enter(StoryDefinition d, NarrativeState s, string target)
        {
            int budget = d.Nodes.Count + 1;
            while (true)
            {
                if (--budget < 0) throw new ArgumentException("Automatic story transition budget exceeded.");
                var node = d.Nodes.Single(n => n.Id == target); s.Run.Current = target;
                Apply(d, s.Run, node.Effects);
                if (!s.Run.Visited.Contains(target)) s.Run.Visited.Add(target);
                foreach (var id in node.Journal)
                {
                    if (s.Run.Journal.ContainsKey(id)) continue;
                    var entry = d.Journal.Single(j => j.Id == id);
                    s.Run.Journal[id] = Resolve(d, s.Run, entry.TitleKey) + "\n" + Resolve(d, s.Run, entry.TextKey);
                    s.Run.Receipt.Add("Evidence recorded: " + Resolve(d, s.Run, entry.TitleKey));
                }
                if (node.Kind == StoryNodeKind.Branch)
                { target = node.Branches.FirstOrDefault(b => Test(d, s.Run, b.Condition))?.Target ?? node.Next; continue; }
                string text = node.Variants.FirstOrDefault(v => Test(d, s.Run, v.Condition))?.TextKey ?? node.TextKey;
                s.Run.Transcript.Add(new TranscriptEntry { Node = node.Id, Speaker = node.Speaker, Chapter = node.Chapter,
                    Subject = Resolve(d, s.Run, node.SubjectKey), Body = Resolve(d, s.Run, text),
                    EvidenceIds = Regex.Matches(d.Text[text], @"\[\[([\w.-]+)\|([^\]]+)\]\]").Cast<Match>().Select(m=>m.Groups[1].Value).Where(id=>s.Run.Journal.ContainsKey(id)).Distinct().ToList() });
                if (node.Checkpoint) s.Checkpoints[node.Id] = FeatureJson.Copy(s.Run);
                if (node.Kind == StoryNodeKind.Ending)
                { s.Won = true; if (!s.Endings.Contains(node.Id)) s.Endings.Add(node.Id); }
                s.Message = node.Kind == StoryNodeKind.Ending ? "Ending recorded." : "Correspondence received."; break;
            }
        }
        protected override void ValidateState(NarrativeState s)
        {
            base.ValidateState(s);
            FeatureJson.Require(s.Run?.Variables != null && s.Run.Transcript != null && s.Run.Visited != null && s.Run.Journal != null && s.Checkpoints != null && s.Endings != null && s.Revision >= 0, "Invalid narrative state.");
            if (Definition == null) return;
            void Run(StoryRun run)
            {
                FeatureJson.Require(Definition.Nodes.Any(n => n.Id == run.Current) && run.Variables.Count == Definition.Variables.Count
                    && Definition.Variables.All(v => run.Variables.ContainsKey(v.Id) && run.Variables[v.Id]?.Members != null)
                    && run.Transcript.All(t => Definition.Nodes.Any(n => n.Id == t.Node)) && run.Journal.Keys.All(id => Definition.Journal.Any(j => j.Id == id)), "Narrative save references unknown content.");
            }
            Run(s.Run); foreach (var checkpoint in s.Checkpoints.Values) Run(checkpoint);
            FeatureJson.Require(s.Endings.All(id => Definition.Nodes.Any(n => n.Id == id && n.Kind == StoryNodeKind.Ending)), "Unknown ending.");
        }
    }
    public sealed class InvestigationDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public double Seconds { get; set; }
        public double AvailableFrom { get; set; }
        public double AvailableUntil { get; set; } = 720;
        public string ResultNode { get; set; }
        public StoryCondition Requirement { get; set; } = new StoryCondition();
    }
    public sealed class TimeLoopModule : NarrativeModule
    {
        public const double LoopSeconds = 720;
        public InvestigationDefinition[] Investigations { get; }
        public override bool RunsOffline => true;
        public override bool IsTimeBlocked { get { var s=State;return s.AwaitingDecision || s.AtLoopBoundary || s.Won; } }
        public override double? AdvanceLimit
        { get { var s=State;return s.AwaitingDecision || s.AtLoopBoundary ? s.PauseTime : (double?)null; } }
        public TimeLoopModule(StoryDefinition d, InvestigationDefinition[] investigations) : base(d, new { Story = StoryCompiler.Compile(d), Investigations = investigations, LoopSeconds })
        {
            FeatureJson.Require(investigations != null && investigations.Select(i=>i.Id).Distinct().Count()==investigations.Length
                && investigations.All(i=>i.Seconds>0 && FeatureJson.Finite(i.Seconds) && i.AvailableFrom>=0 && i.AvailableUntil<=LoopSeconds
                    && i.AvailableUntil>i.AvailableFrom && d.Nodes.Any(n=>n.Id==i.ResultNode)), "Invalid investigations.");
            Investigations = FeatureJson.Copy(investigations);
        }
        public override void Register(IncrementalFramework.Progression.ModuleBuilder builder)
        {
            base.Register(builder);
            builder.Configure(sim => { var result = sim.Execute(new FeatureCommand("begin-loop")); if (!result.Success) throw new ArgumentException(result.Error); });
        }
        protected override string Act(NarrativeState s, FeatureCommand c, double time)
        {
            if (c.Action == "restart")
            {
                var result = base.Act(s,c,time);
                if(result != null) return result;
                s.Loop=0; s.Events.Clear(); s.Deadline=time+LoopSeconds; s.AtLoopBoundary=false; s.AwaitingDecision=false; s.Investigation=null;
                s.Schedule(s.Deadline,"bell",priority:10); return null;
            }
            if (c.Action == "begin-loop" || c.Action == "reset-loop")
            {
                if (c.Action == "reset-loop" && !s.AtLoopBoundary) return "Wait for the thirteenth bell.";
                if (c.Action == "reset-loop")
                {
                    foreach (var v in Definition.Variables.Where(v => !v.SurvivesLoop)) s.Run.Variables[v.Id] = FeatureJson.Copy(v.Initial);
                    s.Loop++; s.Revision++; s.Won = false; Enter(Definition, s, Definition.Start);
                }
                s.Events.Clear(); s.Deadline = time + LoopSeconds; s.AtLoopBoundary = false; s.AwaitingDecision = false; s.Investigation = null;
                s.Schedule(s.Deadline, "bell", priority: 10); return null;
            }
            if (c.Action == "choose" && c.Target.StartsWith("investigate:", StringComparison.Ordinal))
            {
                if (c.Argument != s.Run.Current + ":" + s.Revision) return "Stale investigation choice.";
                c = new FeatureCommand("investigate", c.Target.Substring(12));
            }
            if (c.Action == "wait-bell")
            {
                if (s.Investigation != null || s.AtLoopBoundary) return "Cannot wait now.";
                s.AwaitingDecision = false; return null;
            }
            if (c.Action == "investigate")
            {
                var investigation = Investigations.FirstOrDefault(i => i.Id == c.Target);
                if (Definition.Nodes.Single(n=>n.Id==s.Run.Current).Kind != StoryNodeKind.Choice || s.AtLoopBoundary || s.Investigation != null || s.Won || investigation == null || !Test(Definition, s.Run, investigation.Requirement)) return "Investigation unavailable.";
                double elapsed = time - (s.Deadline - LoopSeconds);
                if (elapsed < investigation.AvailableFrom || elapsed >= investigation.AvailableUntil) return "That appointment is not available at this time.";
                double duration = s.Run.Variables.TryGetValue("shortcuts", out var shortcuts) && shortcuts.Members.Contains(investigation.Id) ? investigation.Seconds * 0.5 : investigation.Seconds;
                if (elapsed + duration > investigation.AvailableUntil) return "The appointment ends before this investigation can finish.";
                if (time + duration > s.Deadline) return "Not enough time before the bell.";
                s.AwaitingDecision = false; s.Investigation = investigation.Id; s.Schedule(time + duration, "investigation", investigation.Id);
                s.Message = "Investigating " + investigation.Name + "."; return null;
            }
            if (s.Investigation != null || s.AtLoopBoundary) return "Finish the investigation or restart the loop first.";
            var error = base.Act(s, c, time);
            if (error == null) { s.AwaitingDecision = true; s.PauseTime = time; }
            return error;
        }
        protected override void OnEvent(NarrativeState s, FeatureEvent e, double time)
        {
            if (e.Kind == "bell")
            { s.Revision++; s.AtLoopBoundary = true; s.AwaitingDecision = false; s.PauseTime = time; s.Investigation = null; s.Message = "The thirteenth bell rings. Restart to retain your knowledge."; }
            else if (e.Kind == "investigation")
            {
                if (s.Investigation != e.Target) return;
                var investigation = Investigations.Single(i => i.Id == e.Target); s.Run.Receipt.Clear();
                Enter(Definition, s, investigation.ResultNode); s.Revision++; s.Investigation = null; s.AwaitingDecision = true; s.PauseTime = time;
            }
        }
    }
}
