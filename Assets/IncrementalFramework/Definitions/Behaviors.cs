using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;

namespace IncrementalFramework.Definitions
{
    public readonly struct EventKey<T>
    {public string Id{get;}public EventKey(string id){Id=id??throw new ArgumentNullException(nameof(id));}}
    public abstract class BehaviorNode
    {internal abstract void Compile(List<BehaviorInstruction> instructions);}
    public static class Behavior
    {
        public static BehaviorNode Sequence(params BehaviorNode[] nodes)=>new SequenceNode(nodes);
        public static BehaviorNode Action(string id)=>new ActionNode(id);
        public static BehaviorNode Delay(double seconds)=>seconds<0||double.IsNaN(seconds)||double.IsInfinity(seconds)?throw new ArgumentOutOfRangeException(nameof(seconds)):new DelayNode(seconds);
        public static BehaviorNode If(Expression<bool> condition,BehaviorNode yes,BehaviorNode no=null)=>new IfNode(condition,yes,no);
        public static BehaviorNode ForEach(TargetQuery query,BehaviorNode body)=>new ForEachNode(query,body);
        public static BehaviorNode Repeat(double interval,BehaviorNode body)=>interval<=0?throw new ArgumentOutOfRangeException(nameof(interval)):new RepeatNode(interval,body);
        public static BehaviorNode Transition(PropertyKey<string> property,string state)=>new EffectNode(Effects.Set(property,Expr.Constant(state)));
        public static BehaviorNode Effect(EffectDefinition effect)=>new EffectNode(effect);
        private sealed class SequenceNode:BehaviorNode
        {private readonly BehaviorNode[] nodes;public SequenceNode(BehaviorNode[]nodes){this.nodes=(BehaviorNode[])nodes.Clone();}internal override void Compile(List<BehaviorInstruction> code){foreach(var node in nodes)node.Compile(code);}}
        private sealed class ActionNode:BehaviorNode
        {private readonly string id;public ActionNode(string id){this.id=id;}internal override void Compile(List<BehaviorInstruction>code)=>code.Add(new BehaviorInstruction{Kind="action",Action=id});}
        private sealed class DelayNode:BehaviorNode
        {private readonly double seconds;public DelayNode(double seconds){this.seconds=seconds;}internal override void Compile(List<BehaviorInstruction>code)=>code.Add(new BehaviorInstruction{Kind="delay",Delay=seconds});}
        private sealed class EffectNode:BehaviorNode
        {private readonly EffectDefinition effect;public EffectNode(EffectDefinition effect){this.effect=effect;}internal override void Compile(List<BehaviorInstruction>code)=>code.Add(new BehaviorInstruction{Kind="effect",Effect=effect});}
        private sealed class IfNode:BehaviorNode
        {private readonly Expression<bool> condition;private readonly BehaviorNode yes,no;public IfNode(Expression<bool>condition,BehaviorNode yes,BehaviorNode no){this.condition=condition;this.yes=yes;this.no=no;}internal override void Compile(List<BehaviorInstruction>code){var branch=new BehaviorInstruction{Kind="branch",Condition=condition};code.Add(branch);yes.Compile(code);var jump=new BehaviorInstruction{Kind="jump"};code.Add(jump);branch.Jump=code.Count;no?.Compile(code);jump.Jump=code.Count;}}
        private sealed class ForEachNode:BehaviorNode
        {private readonly TargetQuery query;private readonly BehaviorNode body;public ForEachNode(TargetQuery query,BehaviorNode body){this.query=query;this.body=body;}internal override void Compile(List<BehaviorInstruction>code){int start=code.Count;var enter=new BehaviorInstruction{Kind="foreach",Query=query};code.Add(enter);body.Compile(code);code.Add(new BehaviorInstruction{Kind="next",Jump=start+1});enter.Jump=code.Count;}}
        private sealed class RepeatNode:BehaviorNode
        {private readonly double interval;private readonly BehaviorNode body;public RepeatNode(double interval,BehaviorNode body){this.interval=interval;this.body=body;}internal override void Compile(List<BehaviorInstruction>code){int start=code.Count;body.Compile(code);code.Add(new BehaviorInstruction{Kind="delay",Delay=interval});code.Add(new BehaviorInstruction{Kind="jump",Jump=start});}}
    }
    public sealed class BehaviorInstruction
    {
        public string Kind{get;internal set;}
        public string Action{get;internal set;}
        public double Delay{get;internal set;}
        public int Jump{get;internal set;}
        public Expression<bool> Condition{get;internal set;}
        public TargetQuery Query{get;internal set;}
        public EffectDefinition Effect{get;internal set;}
    }
    public sealed class BehaviorDefinition
    {
        public string Id{get;}
        public IReadOnlyList<BehaviorInstruction> Instructions{get;}
        public string Signature=>string.Join("|",Instructions.Select(x=>x.Kind+":"+x.Action+":"+x.Delay.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":"+x.Jump));
        public BehaviorDefinition(string id,BehaviorNode root){Id=id;var code=new List<BehaviorInstruction>();root.Compile(code);Instructions=code.AsReadOnly();}
        internal void Validate(IReadOnlyDictionary<string,ActionDefinition> actions)
        {foreach(var instruction in Instructions)if(instruction.Kind=="action"&&!actions.ContainsKey(instruction.Action))throw new ArgumentException(Id+": unknown action "+instruction.Action);}
    }
    [Serializable]
    public sealed class BehaviorLoop
    {public string[] Targets;public int Index;public string PreviousTarget;}
    [Serializable]
    public sealed class BehaviorJob
    {
        public long Sequence;public string BehaviorId;public string ActorId;public string TargetId;public string SourceId;
        public int ProgramCounter;public double WakeTime;public string Error;
        public List<BehaviorLoop> Loops=new List<BehaviorLoop>();
        public Dictionary<string,object> Arguments=new Dictionary<string,object>();
    }
    public sealed class BehaviorRunner
    {
        private readonly CompiledGameDefinition definition;
        private readonly EntityContext world;
        private readonly List<BehaviorJob> jobs=new List<BehaviorJob>();
        private readonly Dictionary<string,List<(string Template,string Behavior)>> triggers=new Dictionary<string,List<(string,string)>>(StringComparer.Ordinal);
        public double Time{get;private set;}
        public long NextSequence{get;private set;}=1;
        public int LastProcessedWork{get;private set;}
        public IReadOnlyList<BehaviorJob> Jobs=>jobs.AsReadOnly();
        public IReadOnlyDictionary<string,string> ProgramSignatures=>definition.Behaviors.ToDictionary(x=>x.Key,x=>x.Value.Signature);
        public Func<double,int,AdvanceResult> AdvanceBoundary { get; set; }
        public Func<double> SourceClock { get; set; }
        public BehaviorRunner(CompiledGameDefinition definition,EntityContext world)
        {
            this.definition=definition;this.world=world;
            world.Added+=entity=>Emit("entity.created",entity.Id);
            world.Removed+=entity=>jobs.RemoveAll(x=>x.ActorId==entity.Id);
            world.ValueChanged+=(value,before,after)=>{if(value.Owner is EntityInstance actor)Emit("value."+value.Name,actor.Id);};
        }
        public void On<T>(EventKey<T> key,string templateId,string behaviorId)=>On(key.Id,templateId,behaviorId);
        public void On(string eventId,string templateId,string behaviorId)
        {
            if(!definition.Behaviors.ContainsKey(behaviorId))throw new ArgumentException("Unknown behavior "+behaviorId);
            if(!triggers.TryGetValue(eventId,out var list)){list=new List<(string,string)>();triggers.Add(eventId,list);}list.Add((templateId,behaviorId));
        }
        public void Emit<T>(EventKey<T> key,string actorId,T payload){Emit(key.Id,actorId,new Dictionary<string,object>{{"event",payload}});}
        public void Emit(string eventId,string actorId,Dictionary<string,object> arguments=null)
        {
            if(!world.Entities.TryGetValue(actorId,out var actor)||!triggers.TryGetValue(eventId,out var list))return;
            foreach(var trigger in list)if(trigger.Template==null||trigger.Template==actor.TemplateId)Start(trigger.Behavior,actorId,arguments);
        }
        public BehaviorJob Start(string behaviorId,string actorId,Dictionary<string,object> arguments=null)
        {
            if(!definition.Behaviors.ContainsKey(behaviorId))throw new ArgumentException("Unknown behavior "+behaviorId);
            world.Find(actorId);
            var job=new BehaviorJob{BehaviorId=behaviorId,ActorId=actorId,TargetId=actorId,SourceId=actorId,WakeTime=SourceClock==null?Time:SourceClock(),Sequence=NextSequence++,Arguments=arguments==null?new Dictionary<string,object>():new Dictionary<string,object>(arguments)};
            jobs.Add(job);return job;
        }
        public bool AdvanceTo(double target,int stepBudget=1024)
        {
            if(double.IsNaN(target)||double.IsInfinity(target)||target<Time||stepBudget<1)throw new ArgumentOutOfRangeException(nameof(target));
            int steps=0;
            try
            {
            while(true)
            {
                var job=jobs.Where(x=>x.WakeTime<=target).OrderBy(x=>x.WakeTime).ThenBy(x=>x.Sequence).FirstOrDefault();
                if(steps>=stepBudget)return false;
                double boundary=job==null?target:job.WakeTime;
                if(AdvanceBoundary!=null)
                {
                    var result=AdvanceBoundary(boundary,stepBudget-steps);steps+=result.ProcessedWork;Time=result.Time;
                    if(!result.Completed)return false;
                }
                else Time=boundary;
                if(job==null)
                {if(jobs.Any(x=>x.WakeTime<=target))continue;return true;}
                if(!jobs.Contains(job))continue;
                if(!world.Entities.TryGetValue(job.ActorId,out var actor)){jobs.Remove(job);continue;}
                if(!world.Entities.TryGetValue(job.TargetId,out var targetEntity)||!world.Entities.TryGetValue(job.SourceId,out var sourceEntity))
                {jobs.Remove(job);job.Error="A continuation reference was removed.";world.Warn("behavior-cancelled",job.Error);continue;}
                var context=new EvaluationContext(actor,definition.Extensions){Time=Time,Target=targetEntity,Source=sourceEntity};
                foreach(var pair in job.Arguments)context.Arguments.Add(pair.Key,pair.Value);
                var transaction=new EntityTransaction();context.Transaction=transaction;
                var code=definition.Behaviors[job.BehaviorId].Instructions;
                bool paused=false;
                try
                {
                    while(job.ProgramCounter<code.Count)
                    {
                        if(steps>=stepBudget){paused=true;break;}
                        steps++;
                        var instruction=code[job.ProgramCounter++];
                        switch(instruction.Kind)
                        {
                            case "action":
                                var action=definition.Actions[instruction.Action];
                                if(!action.Requirement.Evaluate(context))throw new ActionRejectedException("Requirements failed for "+action.Id);
                                foreach(var effect in action.Effects)effect.Stage(context,transaction);break;
                            case "effect":instruction.Effect.Stage(context,transaction);break;
                            case "branch":if(!instruction.Condition.Evaluate(context))job.ProgramCounter=instruction.Jump;break;
                            case "jump":job.ProgramCounter=instruction.Jump;break;
                            case "delay":job.WakeTime=Time+instruction.Delay;paused=true;break;
                            case "foreach":
                                var targets=instruction.Query.Select(context).Select(x=>x.Id).ToArray();
                                if(targets.Length==0){job.ProgramCounter=instruction.Jump;break;}
                                job.Loops.Add(new BehaviorLoop{Targets=targets,Index=0,PreviousTarget=context.Target.Id});context.Target=world.Find(targets[0]);break;
                            case "next":
                                var loop=job.Loops[job.Loops.Count-1];loop.Index++;
                                if(loop.Index<loop.Targets.Length){context.Target=world.Find(loop.Targets[loop.Index]);job.ProgramCounter=instruction.Jump;}
                                else{context.Target=world.Find(loop.PreviousTarget);job.Loops.RemoveAt(job.Loops.Count-1);}break;
                        }
                        if(paused)break;
                    }
                    if(!transaction.Commit())throw new ActionRejectedException("Stale behavior transaction.");
                    job.TargetId=context.Target.Id;
                    if(job.ProgramCounter>=code.Count)jobs.Remove(job);
                }
                catch(ActionRejectedException error){job.Error=error.Message;jobs.Remove(job);world.Warn("behavior-rejected",job.BehaviorId+": "+error.Message);}
            }
            }
            finally { LastProcessedWork=steps; }
        }
        public void Restore(double time,long sequence,IEnumerable<BehaviorJob> restored)
        {
            var incoming=restored.ToArray();
            if(double.IsNaN(time)||double.IsInfinity(time)||time<0||sequence<1||incoming.Select(x=>x.Sequence).Distinct().Count()!=incoming.Length)throw new ArgumentException("Invalid behavior snapshot.");
            foreach(var job in incoming)
            {
                if(!definition.Behaviors.TryGetValue(job.BehaviorId,out var behavior)||job.ProgramCounter<0||job.ProgramCounter>behavior.Instructions.Count||job.Sequence<1||job.Sequence>=sequence||job.WakeTime<time||double.IsNaN(job.WakeTime)||double.IsInfinity(job.WakeTime))throw new ArgumentException("Invalid behavior continuation.");
                world.Find(job.ActorId);world.Find(job.TargetId);world.Find(job.SourceId);
                if(job.Loops==null||job.Arguments==null)throw new ArgumentException("Invalid continuation frame.");
                foreach(var loop in job.Loops)
                {
                    if(loop==null||loop.Targets==null||loop.Index<0||loop.Index>=loop.Targets.Length||string.IsNullOrEmpty(loop.PreviousTarget))throw new ArgumentException("Invalid continuation loop.");
                    world.Find(loop.PreviousTarget);foreach(var id in loop.Targets)world.Find(id);
                }
            }
            jobs.Clear();jobs.AddRange(incoming);Time=time;NextSequence=sequence;
        }
    }
}
