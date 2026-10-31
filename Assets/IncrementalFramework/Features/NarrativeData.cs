using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;

namespace IncrementalFramework.Features.Narrative
{
    public enum StoryVariableKind { Boolean, Integer, Set }
    public enum StoryNodeKind { Passage, Choice, Branch, Ending }
    public enum ConditionKind { Always, Boolean, Equal, AtLeast, Contains, All, Any, Not }
    public enum StoryEffectKind { SetBoolean, SetInteger, AddInteger, Include, Remove }
    public enum UnavailableChoice { Hidden, Locked }
    public sealed class StoryValue
    {
        public bool Boolean { get; set; }
        public int Integer { get; set; }
        public List<string> Members { get; set; } = new List<string>();
    }
    public sealed class StoryVariable
    {
        public string Id { get; set; }
        public StoryVariableKind Kind { get; set; }
        public StoryValue Initial { get; set; } = new StoryValue();
        public bool SurvivesLoop { get; set; }
    }
    public sealed class StoryCondition
    {
        public ConditionKind Kind { get; set; }
        public string Variable { get; set; }
        public bool Boolean { get; set; } = true;
        public int Integer { get; set; }
        public string Member { get; set; }
        public List<StoryCondition> Children { get; set; } = new List<StoryCondition>();
        public Expression<bool> Compile()
        {
            switch (Kind)
            {
                case ConditionKind.Always: return Expr.Constant(true);
                case ConditionKind.Boolean: return Expr.Equal(Expr.Property(new PropertyKey<bool>(Variable)), Expr.Constant(Boolean));
                case ConditionKind.Equal: return Expr.Equal(Expr.Property(new PropertyKey<int>(Variable)), Expr.Constant(Integer));
                case ConditionKind.AtLeast: return Expr.AtLeast(Expr.Property(new PropertyKey<int>(Variable)), Expr.Constant(Integer));
                case ConditionKind.Contains: return new ContainsExpression(Variable, Member);
                case ConditionKind.All: return Expr.And(Children.Select(c => c.Compile()).ToArray());
                case ConditionKind.Any: return Expr.Or(Children.Select(c => c.Compile()).ToArray());
                case ConditionKind.Not: return Expr.Not(Children.Single().Compile());
                default: throw new ArgumentException("Unknown story condition.");
            }
        }
        private sealed class ContainsExpression : Expression<bool>
        {
            private readonly string variable, member;
            public ContainsExpression(string variable, string member) { this.variable = variable; this.member = member; }
            public override IEnumerable<PropertyReference> References => new[] { new PropertyReference(variable, typeof(string[]), EntityScope.Self) };
            public override bool Evaluate(EvaluationContext c) => c.Read(c.Self, new PropertyKey<string[]>(variable)).Contains(member);
        }
    }
    public sealed class StoryEffect
    {
        public StoryEffectKind Kind { get; set; }
        public string Variable { get; set; }
        public bool Boolean { get; set; }
        public int Integer { get; set; }
        public string Member { get; set; }
        public string DisclosureKey { get; set; }
    }
    public sealed class StoryChoice
    {
        public string Id { get; set; }
        public string TextKey { get; set; }
        public string Target { get; set; }
        public StoryCondition Requirement { get; set; } = new StoryCondition();
        public UnavailableChoice Unavailable { get; set; }
        public string DisabledReasonKey { get; set; }
        public string HintKey { get; set; }
        public List<StoryEffect> Effects { get; set; } = new List<StoryEffect>();
    }
    public sealed class StoryBranch
    {
        public StoryCondition Condition { get; set; } = new StoryCondition();
        public string Target { get; set; }
    }
    public sealed class StoryTextVariant
    {
        public StoryCondition Condition { get; set; } = new StoryCondition();
        public string TextKey { get; set; }
    }
    public sealed class StoryNode
    {
        public string Id { get; set; }
        public StoryNodeKind Kind { get; set; }
        public int Chapter { get; set; } = 1;
        public string Speaker { get; set; }
        public string SubjectKey { get; set; }
        public string TextKey { get; set; }
        public string Next { get; set; }
        public bool Checkpoint { get; set; }
        public List<StoryTextVariant> Variants { get; set; } = new List<StoryTextVariant>();
        public List<StoryChoice> Choices { get; set; } = new List<StoryChoice>();
        public List<StoryBranch> Branches { get; set; } = new List<StoryBranch>();
        public List<StoryEffect> Effects { get; set; } = new List<StoryEffect>();
        public List<string> Journal { get; set; } = new List<string>();
    }
    public sealed class StoryCharacter
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string ProfileKey { get; set; }
    }
    public sealed class StoryJournalEntry
    {
        public string Id { get; set; }
        public string TitleKey { get; set; }
        public string TextKey { get; set; }
    }
    public sealed class StoryDefinition
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Start { get; set; }
        public List<StoryVariable> Variables { get; set; } = new List<StoryVariable>();
        public List<StoryNode> Nodes { get; set; } = new List<StoryNode>();
        public List<StoryCharacter> Characters { get; set; } = new List<StoryCharacter>();
        public List<StoryJournalEntry> Journal { get; set; } = new List<StoryJournalEntry>();
        public Dictionary<string, string> Text { get; set; } = new Dictionary<string, string>();
    }
    public sealed class StoryDiagnostic
    {
        public string Node { get; set; }
        public string Message { get; set; }
        public override string ToString() => (Node ?? "Story") + ": " + Message;
    }

    public static class StoryCompiler
    {
        public static StoryDefinition Compile(StoryDefinition story)
        {
            var errors = Validate(story);
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
            return FeatureJson.Copy(story);
        }
        public static List<StoryDiagnostic> Validate(StoryDefinition d)
        {
            var errors = new List<StoryDiagnostic>();
            void Error(string id, string text) => errors.Add(new StoryDiagnostic { Node = id, Message = text });
            if (d == null || d.Nodes == null || d.Text == null || d.Variables == null || d.Characters == null || d.Journal == null)
            { Error(null, "Missing story collections."); return errors; }
            if(d.Characters.Any(c=>c==null || string.IsNullOrWhiteSpace(c.Id)) || d.Characters.Select(c=>c.Id).Distinct().Count()!=d.Characters.Count
                || d.Journal.Any(j=>j==null || string.IsNullOrWhiteSpace(j.Id)) || d.Journal.Select(j=>j.Id).Distinct().Count()!=d.Journal.Count)
            { Error(null,"Missing or duplicate character/journal IDs."); return errors; }
            if(d.Text.Any(p=>string.IsNullOrWhiteSpace(p.Key) || p.Value==null)) Error(null,"Text keys and values must be present.");
            var nodes = new Dictionary<string, StoryNode>(StringComparer.Ordinal);
            foreach (var n in d.Nodes)
                if (n == null || string.IsNullOrWhiteSpace(n.Id) || nodes.ContainsKey(n.Id)) Error(n?.Id, "Missing or duplicate node ID.");
                else nodes.Add(n.Id, n);
            var variables = new Dictionary<string, StoryVariable>(StringComparer.Ordinal);
            foreach (var v in d.Variables)
                if (v == null || string.IsNullOrWhiteSpace(v.Id) || variables.ContainsKey(v.Id) || v.Initial == null) Error(null, "Missing or duplicate variable.");
                else
                { if(!Enum.IsDefined(typeof(StoryVariableKind),v.Kind) || v.Initial.Members==null) Error(null,"Invalid variable type or initial set."); variables.Add(v.Id,v); }
            if (string.IsNullOrWhiteSpace(d.Id)) Error(null, "Story ID required.");
            if (d.Start == null || !nodes.ContainsKey(d.Start)) Error(null, "Start node does not exist.");
            void Text(string node, string key, bool optional = false)
            { if (!(optional && string.IsNullOrEmpty(key)) && (key == null || !d.Text.ContainsKey(key))) Error(node, "Missing text: " + key);
                if(key!=null && d.Text.TryGetValue(key,out var body) && body!=null)
                    foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(body,@"\[\[([\w.-]+)\|([^\]]+)\]\]"))
                        if(!d.Journal.Any(j=>j.Id==match.Groups[1].Value)) Error(node,"Unknown inline evidence: "+match.Groups[1].Value); }
            void Target(string node, string target)
            { if (target == null || !nodes.ContainsKey(target)) Error(node, "Missing target: " + target); }
            void Condition(string node, StoryCondition c, int depth = 0)
            {
                if (c == null || depth > 32) { Error(node, "Missing or excessively nested condition."); return; }
                StoryVariableKind? kind = c.Kind == ConditionKind.Boolean ? StoryVariableKind.Boolean : c.Kind == ConditionKind.Contains ? StoryVariableKind.Set
                    : c.Kind == ConditionKind.Equal || c.Kind == ConditionKind.AtLeast ? StoryVariableKind.Integer : (StoryVariableKind?)null;
                if (kind.HasValue && (c.Variable == null || !variables.TryGetValue(c.Variable, out var v) || v.Kind != kind.Value)) Error(node, "Condition variable type mismatch: " + c.Variable);
                if (c.Kind == ConditionKind.Contains && string.IsNullOrEmpty(c.Member)) Error(node, "Set member required.");
                if (c.Children == null) { Error(node, "Missing condition children."); return; }
                if (c.Kind == ConditionKind.Not && c.Children.Count != 1 || (c.Kind == ConditionKind.All || c.Kind == ConditionKind.Any) && c.Children.Count == 0) Error(node, "Invalid condition operands.");
                foreach (var child in c.Children) Condition(node, child, depth + 1);
            }
            void Effects(string node, List<StoryEffect> effects)
            {
                if (effects == null) { Error(node, "Missing effects."); return; }
                foreach (var e in effects)
                {
                    if (e == null) { Error(node, "Missing effect."); continue; }
                    var kind = e.Kind == StoryEffectKind.SetBoolean ? StoryVariableKind.Boolean : e.Kind == StoryEffectKind.SetInteger || e.Kind == StoryEffectKind.AddInteger ? StoryVariableKind.Integer : StoryVariableKind.Set;
                    if (e.Variable == null || !variables.TryGetValue(e.Variable, out var v) || v.Kind != kind) Error(node, "Effect variable type mismatch: " + e.Variable);
                    if (kind == StoryVariableKind.Set && string.IsNullOrEmpty(e.Member)) Error(node, "Effect set member required.");
                    Text(node, e.DisclosureKey, true);
                }
            }
            var choiceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in nodes.Values)
            {
                if (!Enum.IsDefined(typeof(StoryNodeKind),n.Kind)) Error(n.Id,"Unknown node kind.");
                if (n.Choices == null || n.Branches == null || n.Variants == null || n.Journal == null || n.Choices.Any(c=>c==null) || n.Branches.Any(b=>b==null) || n.Variants.Any(v=>v==null)) { Error(n.Id, "Missing node collections."); continue; }
                if (n.Kind != StoryNodeKind.Branch) { Text(n.Id, n.TextKey); Text(n.Id, n.SubjectKey); }
                if (!string.IsNullOrEmpty(n.Speaker) && !d.Characters.Any(c => c.Id == n.Speaker)) Error(n.Id, "Unknown speaker.");
                if (n.Kind == StoryNodeKind.Passage || n.Kind == StoryNodeKind.Branch) Target(n.Id, n.Next);
                if (n.Kind == StoryNodeKind.Choice && n.Choices.Count == 0) Error(n.Id, "Choice node has no responses.");
                Effects(n.Id, n.Effects);
                foreach (var variant in n.Variants) { Condition(n.Id, variant.Condition); Text(n.Id, variant.TextKey); }
                foreach (var id in n.Journal) if (!d.Journal.Any(j => j.Id == id)) Error(n.Id, "Unknown journal entry: " + id);
                foreach (var c in n.Choices)
                {
                    if (string.IsNullOrWhiteSpace(c.Id) || !choiceIds.Add(c.Id)) Error(n.Id, "Missing or duplicate choice ID.");
                    Text(n.Id, c.TextKey); Text(n.Id, c.HintKey, true); Condition(n.Id, c.Requirement); Target(n.Id, c.Target); Effects(n.Id, c.Effects);
                    if (c.Unavailable == UnavailableChoice.Locked) Text(n.Id, c.DisabledReasonKey);
                }
                foreach (var b in n.Branches) { Condition(n.Id, b.Condition); Target(n.Id, b.Target); }
            }
            foreach (var c in d.Characters) Text(null, c.ProfileKey);
            foreach (var j in d.Journal) { Text(null, j.TitleKey); Text(null, j.TextKey); }
            var visited = new HashSet<string>();
            IEnumerable<string> Edges(StoryNode n) => n.Kind == StoryNodeKind.Choice ? n.Choices.Select(c => c.Target)
                : n.Kind == StoryNodeKind.Branch ? n.Branches.Select(b => b.Target).Concat(new[] { n.Next })
                : n.Kind == StoryNodeKind.Passage ? new[] { n.Next } : Array.Empty<string>();
            void Visit(string id)
            { if (id == null || !nodes.ContainsKey(id) || !visited.Add(id)) return; foreach (var next in Edges(nodes[id])) Visit(next); }
            if (errors.Count == 0)
            {
                Visit(d.Start); foreach (var n in nodes.Values.Where(n => !visited.Contains(n.Id))) Error(n.Id, "Unreachable node.");
                var visiting = new HashSet<string>(); var done = new HashSet<string>();
                void Cycle(string id)
                {
                    if (!nodes.TryGetValue(id, out var n) || n.Kind != StoryNodeKind.Branch || done.Contains(id)) return;
                    if (!visiting.Add(id)) { Error(id, "Automatic-transition cycle."); return; }
                    foreach (var next in Edges(n)) Cycle(next);
                    visiting.Remove(id); done.Add(id);
                }
                foreach (var id in nodes.Keys) Cycle(id);
                if (!nodes.Values.Any(n => n.Kind == StoryNodeKind.Ending)) Error(null, "Story needs an ending.");
            }
            return errors;
        }
    }
}
