using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;

namespace IncrementalFramework.Definitions
{
    public sealed class TargetQuery
    {
        private readonly string source;
        private readonly string related;
        private readonly Expression<bool> predicate;
        private readonly Expression<double> order;
        private readonly bool descending;
        private readonly int take;
        private string kind;
        private Expression<string> identity;
        public IEnumerable<string> RequiredExtensions => (predicate?.RequiredExtensions ?? Enumerable.Empty<string>()).Concat(order?.RequiredExtensions ?? Enumerable.Empty<string>()).Concat(identity?.RequiredExtensions ?? Enumerable.Empty<string>()).Concat(source=="custom"?new[]{related}:Array.Empty<string>());
        public IEnumerable<PropertyReference> References => (predicate?.References ?? Enumerable.Empty<PropertyReference>()).Concat(order?.References ?? Enumerable.Empty<PropertyReference>());
        private TargetQuery(string source,string related=null,Expression<bool> predicate=null,Expression<double> order=null,bool descending=false,int take=int.MaxValue)
        {this.source=source;this.related=related;this.predicate=predicate;this.order=order;this.descending=descending;this.take=take;}
        public TargetQuery OfKind(string value) { var copy = new TargetQuery(source,related,predicate,order,descending,take); copy.kind=value;copy.identity=identity; return copy; }
        public static TargetQuery ById(Expression<string> id) => new TargetQuery("id"){identity=id};
        public static TargetQuery Owner()=>new TargetQuery("owner");
        public static TargetQuery Self()=>new TargetQuery("self");
        public static TargetQuery Current()=>new TargetQuery("target");
        public static TargetQuery Source()=>new TargetQuery("source");
        public static TargetQuery All()=>new TargetQuery("all");
        public static TargetQuery Related(PropertyKey<IReadOnlyList<string>> property)=>new TargetQuery("related",property.Name);
        public static TargetQuery Custom(string id)=>new TargetQuery("custom",id);
        public TargetQuery Where(Expression<bool> condition)=>new TargetQuery(source,related,predicate==null?condition:Expr.And(predicate,condition),order,descending,take){kind=kind,identity=identity};
        public TargetQuery OrderBy(Expression<double> value,bool highestFirst=false)=>new TargetQuery(source,related,predicate,value,highestFirst,take){kind=kind,identity=identity};
        public TargetQuery Take(int count)=>count<0?throw new ArgumentOutOfRangeException(nameof(count)):new TargetQuery(source,related,predicate,order,descending,count){kind=kind,identity=identity};
        private static EntityInstance ResolveIdentity(EvaluationContext context,string id)
        {
            if(context.World.Entities.TryGetValue(id,out var exact))return exact;
            return context.World.Find("resource:"+id);
        }
        public IReadOnlyList<EntityInstance> Select(EvaluationContext context)
        {
            IEnumerable<EntityInstance> candidates=source=="id"?new[]{ResolveIdentity(context,identity.Evaluate(context))}:source=="owner"?new[]{context.Resolve(EntityScope.Owner)}:source=="self"?new[]{context.Self}:source=="target"?new[]{context.Target}:source=="source"?new[]{context.Source}
                :source=="related"?context.Self.GetValue<IReadOnlyList<string>>(related).Current.Select(context.World.Find)
                :source=="custom"?context.Extensions.Get<ITargetSelector>(related).Select(context.ReadOnly()).Select(context.World.Find):context.World.Entities.Values;
            var previous=context.Target;
            try
            {
                var selected=new List<(EntityInstance Entity,double Order)>();
                foreach(var entity in candidates)
                {
                    if(kind!=null && entity.Kind!=kind)continue;
                    context.Target=entity;
                    if(predicate!=null&&!predicate.Evaluate(context))continue;
                    selected.Add((entity,order==null?0:order.Evaluate(context)));
                }
                return (descending?selected.OrderByDescending(x=>x.Order):selected.OrderBy(x=>x.Order)).ThenBy(x=>x.Entity.Id,StringComparer.Ordinal).Take(take).Select(x=>x.Entity).ToArray();
            }
            finally{context.Target=previous;}
        }
    }

    public abstract class EffectDefinition
    {
        public abstract IEnumerable<PropertyReference> References {get;}
        public virtual IEnumerable<string> ExtensionIds=>Array.Empty<string>();
        public abstract void Stage(EvaluationContext context,EntityTransaction transaction);
    }
    public static class Effects
    {
        public static EffectDefinition Set<T>(PropertyKey<T> property,Expression<T> amount,TargetQuery targets=null)=>new SetEffect<T>(property,amount,targets??TargetQuery.Self(),false,false);
        public static EffectDefinition Add<T>(PropertyKey<T> property,Expression<T> amount,TargetQuery targets=null)=>new SetEffect<T>(property,amount,targets??TargetQuery.Self(),true,false);
        public static EffectDefinition Spend<T>(PropertyKey<T> property,Expression<T> amount,TargetQuery targets=null)=>new SetEffect<T>(property,amount,targets??TargetQuery.Self(),true,true);
        public static EffectDefinition Attach<T>(string id,string source,PropertyKey<T> property,NumericOperation operation,Expression<T> amount,TargetQuery targets=null,int priority=0)=>new AttachEffect<T>(id,source,property,operation,amount,targets??TargetQuery.Self(),priority);
        public static EffectDefinition RemoveModifiers(string source)=>new RemoveEffect(source);
        public static EffectDefinition Custom(string id)=>new CustomEffect(id);
        public static EffectDefinition Remove(TargetQuery targets)=>new DeleteEffect(targets);
        public static EffectDefinition Create(EntityTemplate template,string prefix)=>new CreateEffect(template,prefix);
        private sealed class SetEffect<T>:EffectDefinition
        {
            private readonly PropertyKey<T> key;private readonly Expression<T> amount;private readonly TargetQuery targets;private readonly bool add,spend;
            public SetEffect(PropertyKey<T>key,Expression<T>amount,TargetQuery targets,bool add,bool spend){this.key=key;this.amount=amount;this.targets=targets;this.add=add;this.spend=spend;}
            public override IEnumerable<string> ExtensionIds=>amount.RequiredExtensions.Concat(targets.RequiredExtensions);
            public override IEnumerable<PropertyReference> References=>amount.References.Concat(targets.References).Concat(new[]{new PropertyReference(key.Name,typeof(T),EntityScope.Target)});
            public override void Stage(EvaluationContext context,EntityTransaction transaction)
            {
                var old=context.Target;
                try{foreach(var target in targets.Select(context))
                {
                    context.Target=target;var value=target.GetValue(key);var delta=amount.Evaluate(context);
                    if(!add){transaction.Set(value,delta);continue;}
                    var numeric=Numeric<T>.Operations;
                    var current=transaction.Read(value);
                    if(spend)
                    {
                        if(numeric.Compare(delta,numeric.Zero)<0||numeric.Compare(current,delta)<0)throw new ActionRejectedException("Insufficient "+key.Name);
                        delta=numeric.Negate(delta);
                    }
                    transaction.Set(value,numeric.Add(current,delta));
                }}finally{context.Target=old;}
            }
        }
        private sealed class AttachEffect<T>:EffectDefinition
        {
            private readonly string id,source;private readonly PropertyKey<T> key;private readonly NumericOperation operation;private readonly Expression<T> amount;private readonly TargetQuery targets;private readonly int priority;
            public AttachEffect(string id,string source,PropertyKey<T>key,NumericOperation operation,Expression<T>amount,TargetQuery targets,int priority){this.id=id;this.source=source;this.key=key;this.operation=operation;this.amount=amount;this.targets=targets;this.priority=priority;}
            public override IEnumerable<string> ExtensionIds=>amount.RequiredExtensions.Concat(targets.RequiredExtensions);
            public override IEnumerable<PropertyReference> References=>amount.References.Concat(targets.References).Concat(new[]{new PropertyReference(key.Name,typeof(T),EntityScope.Target)});
            public override void Stage(EvaluationContext c,EntityTransaction t){var previous=c.Target;try{foreach(var target in targets.Select(c)){c.Target=target;new NumericModifier<T>(id+"@"+c.Self.Id+"@"+target.Id,(source??id)+"@"+c.Self.Id,key,operation,amount.Evaluate(c),priority).Stage(target,t);}}finally{c.Target=previous;}}
        }
        private sealed class RemoveEffect:EffectDefinition
        {private readonly string source;public RemoveEffect(string source){this.source=source;}public override IEnumerable<PropertyReference> References=>Array.Empty<PropertyReference>();public override void Stage(EvaluationContext c,EntityTransaction t){t.IncludeContext(c.World);t.OnCommit(()=>c.World.RemoveModifiers(source));}}
        private sealed class CustomEffect:EffectDefinition
        {private readonly string id;public CustomEffect(string id){this.id=id;}public override IEnumerable<PropertyReference> References=>Array.Empty<PropertyReference>();public override IEnumerable<string> ExtensionIds=>new[]{id};public override void Stage(EvaluationContext c,EntityTransaction t)=>c.Extensions.Get<IEffectHandler>(id).Stage(c.EffectsContext(),t);}
        private sealed class DeleteEffect:EffectDefinition
        {private readonly TargetQuery targets;public DeleteEffect(TargetQuery targets){this.targets=targets;}public override IEnumerable<PropertyReference> References=>targets.References;public override void Stage(EvaluationContext c,EntityTransaction t){foreach(var entity in targets.Select(c))t.Remove(entity);}}
        private sealed class CreateEffect:EffectDefinition
        {private readonly EntityTemplate template;private readonly string prefix;public CreateEffect(EntityTemplate template,string prefix){this.template=template;this.prefix=prefix;}public override IEnumerable<PropertyReference> References=>Array.Empty<PropertyReference>();public override void Stage(EvaluationContext c,EntityTransaction t){var entity=template.Instantiate(new EntityContext(),t.ReserveId(c.World,prefix));t.Create(c.World,entity);}}
    }
    public sealed class ActionRejectedException:Exception{public ActionRejectedException(string message):base(message){}}
    public sealed class ActionDefinition
    {
        public string Id {get;}
        public Expression<bool> Requirement {get;}
        public IReadOnlyList<EffectDefinition> Effects {get;}
        public ActionDefinition(string id,Expression<bool> requirement,params EffectDefinition[] effects)
        {Id=id;Requirement=requirement??Expr.Constant(true);Effects=Array.AsReadOnly((EffectDefinition[])effects.Clone());}
        public bool Execute(EvaluationContext context,out string error)
        {
            var transaction=new EntityTransaction();context.Transaction=transaction;
            try
            {
                if(!Requirement.Evaluate(context)){error="Requirements not met for "+Id;return false;}
                foreach(var effect in Effects)effect.Stage(context,transaction);
                bool committed=transaction.Commit();error=committed?null:"Transaction state changed for "+Id;return committed;
            }
            catch(ActionRejectedException rejected){error=rejected.Message;return false;}
            finally{context.Transaction=null;}
        }
    }
    public sealed class TemplateBuilder
    {
        public EntityTemplate Template {get;}
        private readonly List<Action<EntityInstance>> calculations=new List<Action<EntityInstance>>();
        public TemplateBuilder(string id,ExtensionRegistry extensions=null){Template=new DeclaredTemplate(id,calculations);this.extensions=extensions??new ExtensionRegistry();}
        private readonly ExtensionRegistry extensions;
        public TemplateBuilder Value<T>(PropertyKey<T> key,T initial){Template.Define(key,initial);return this;}
        public TemplateBuilder List<T>(PropertyKey<IReadOnlyList<T>>key,params T[] initial){Template.DefineList(key.Name,initial);return this;}
        public TemplateBuilder Calculated<T>(PropertyKey<T> key,Expression<T> expression)
        {
            Template.Define(key,default(T));
            calculations.Add(instance=>
            {
                foreach(var id in expression.RequiredExtensions)if(!extensions.Contains(id))throw new ArgumentException("Missing extension "+id);
                instance.GetValue(key).Calculate(owner=>expression.Evaluate(new EvaluationContext((EntityInstance)owner,extensions)),
                    expression.References.Where(x=>x.Scope==EntityScope.Self).Select(x=>x.Name).Distinct().ToArray());
            });return this;
        }
        private sealed class DeclaredTemplate:EntityTemplate
        {
            private readonly List<Action<EntityInstance>> calculations;
            public DeclaredTemplate(string id,List<Action<EntityInstance>> calculations):base(id){this.calculations=calculations;}
            public override EntityInstance Instantiate(EntityContext context,string instanceId)
            {var entity=base.Instantiate(context,instanceId);using(context.Batch())foreach(var calculation in calculations)calculation(entity);return entity;}
        }
    }
    public sealed class GameDefinitionBuilder
    {
        private readonly Dictionary<string,TemplateBuilder> templates=new Dictionary<string,TemplateBuilder>(StringComparer.Ordinal);
        private readonly Dictionary<string,ActionDefinition> actions=new Dictionary<string,ActionDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string,BehaviorDefinition> behaviors=new Dictionary<string,BehaviorDefinition>(StringComparer.Ordinal);
        public ExtensionRegistry Extensions {get;}=new ExtensionRegistry();
        public TemplateBuilder Template(string id){if(templates.ContainsKey(id))throw new ArgumentException("Duplicate template "+id);var template=new TemplateBuilder(id,Extensions);templates.Add(id,template);return template;}
        public GameDefinitionBuilder Action(ActionDefinition action){actions.Add(action.Id,action);return this;}
        public GameDefinitionBuilder Behavior(BehaviorDefinition behavior){behaviors.Add(behavior.Id,behavior);return this;}
        public CompiledGameDefinition Compile()
        {
            foreach(var action in actions.Values)
            {
                foreach(var id in action.Requirement.RequiredExtensions.Concat(action.Effects.SelectMany(x=>x.ExtensionIds)))if(!Extensions.Contains(id))throw new ArgumentException(action.Id+": missing extension "+id);
                foreach(var reference in action.Requirement.References.Concat(action.Effects.SelectMany(x=>x.References)))
                    if(!templates.Values.Any(x=>x.Template.Values.TryGetValue(reference.Name,out var value)&&value.DataType==reference.Type))
                        throw new ArgumentException(action.Id+": unknown/incompatible property "+reference.Name);
            }
            foreach(var behavior in behaviors.Values)behavior.Validate(actions);
            // Instantiate once to resolve dependency graphs and detect missing calculation references/cycles.
            foreach(var template in templates.Values)template.Template.Instantiate(new EntityContext(),"validation");
            return new CompiledGameDefinition(templates.ToDictionary(x=>x.Key,x=>x.Value.Template),actions,behaviors,Extensions);
        }
    }
    public sealed class CompiledGameDefinition
    {
        public IReadOnlyDictionary<string,EntityTemplate> Templates {get;}
        public IReadOnlyDictionary<string,ActionDefinition> Actions {get;}
        public IReadOnlyDictionary<string,BehaviorDefinition> Behaviors {get;}
        public ExtensionRegistry Extensions {get;}
        internal CompiledGameDefinition(Dictionary<string,EntityTemplate>templates,Dictionary<string,ActionDefinition>actions,Dictionary<string,BehaviorDefinition>behaviors,ExtensionRegistry extensions)
        {Templates=new Dictionary<string,EntityTemplate>(templates);Actions=new Dictionary<string,ActionDefinition>(actions);Behaviors=new Dictionary<string,BehaviorDefinition>(behaviors);Extensions=extensions;}
    }
}
