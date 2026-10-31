using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;

namespace IncrementalFramework.Definitions
{
    public enum EntityScope { Self, Target, Source, Owner }
    public sealed class EvaluationContext
    {
        public EntityContext World { get; }
        public EntityInstance Self { get; }
        public EntityInstance Target { get; set; }
        public EntityInstance Source { get; set; }
        public double Time { get; set; }
        public ExtensionRegistry Extensions { get; }
        public Dictionary<string, object> Arguments { get; } = new Dictionary<string, object>(StringComparer.Ordinal);
        public EntityTransaction Transaction { get; set; }
        public EvaluationContext(EntityInstance self, ExtensionRegistry extensions = null)
        { Self = self; Target = Source = self; World = self.Context; Extensions = extensions ?? new ExtensionRegistry(); }
        public EntityInstance Resolve(EntityScope scope) => scope == EntityScope.Self ? Self : scope == EntityScope.Target ? Target : scope == EntityScope.Source ? Source : World.Find(Self.GetValue<string>("Owner").Current);
        public T Read<T>(EntityInstance entity, PropertyKey<T> property) => Transaction == null ? entity.GetValue(property).Current : Transaction.Read(entity.GetValue(property));
    }
    public readonly struct PropertyReference
    {
        public string Name { get; }
        public Type Type { get; }
        public EntityScope Scope { get; }
        public PropertyReference(string name, Type type, EntityScope scope) { Name = name; Type = type; Scope = scope; }
    }
    public interface IExpression
    {
        Type ResultType { get; }
        IEnumerable<PropertyReference> References { get; }
        object EvaluateBoxed(EvaluationContext context);
        IEnumerable<string> RequiredExtensions { get; }
    }
    public abstract class Expression<T> : IExpression
    {
        public Type ResultType => typeof(T);
        public virtual IEnumerable<string> RequiredExtensions => Array.Empty<string>();
        public abstract T Evaluate(EvaluationContext context);
        public abstract IEnumerable<PropertyReference> References { get; }
        public object EvaluateBoxed(EvaluationContext context) => Evaluate(context);
    }
    public static class Expr
    {
        public static Expression<T> Constant<T>(T value) => new Literal<T>(value);
        public static Expression<T> Property<T>(PropertyKey<T> property, EntityScope scope = EntityScope.Self) => new PropertyExpression<T>(property, scope);
        public static Expression<T> Argument<T>(string name) => new ArgumentExpression<T>(name);
        public static Expression<string> Identity(EntityScope scope=EntityScope.Self)=>new IdentityExpression(scope);
        public static Expression<int> Divide(Expression<int> a,Expression<int> b)=>new IntDivide(a,b);
        public static Expression<double> Ceiling(Expression<double> value)=>new CeilingExpression(value);
        public static Expression<double> Time() => new TimeExpression();
        public static Expression<T> Add<T>(Expression<T> a, Expression<T> b) => new Binary<T>("add", a, b);
        public static Expression<T> Multiply<T>(Expression<T> a, Expression<T> b) => new Binary<T>("multiply", a, b);
        public static Expression<T> Subtract<T>(Expression<T> a, Expression<T> b) => Add(a, Negate(b));
        public static Expression<T> Min<T>(Expression<T> a, Expression<T> b) => new Binary<T>("min", a, b);
        public static Expression<T> Max<T>(Expression<T> a, Expression<T> b) => new Binary<T>("max", a, b);
        public static Expression<T> Negate<T>(Expression<T> a) => new Unary<T>(a);
        public static Expression<double> Divide(Expression<double> a, Expression<double> b) => new DoubleBinary("divide", a, b);
        public static Expression<double> Power(Expression<double> a, Expression<double> b) => new DoubleBinary("power", a, b);
        public static Expression<int> Modulo(Expression<int> a, Expression<int> b) => new ModuloExpression(a,b);
        public static Expression<double> ToDouble(Expression<int> value) => new ConvertExpression(value);
        public static Expression<bool> Equal<T>(Expression<T> a, Expression<T> b) => new Comparison<T>("equal", a, b);
        public static Expression<bool> AtLeast<T>(Expression<T> a, Expression<T> b) => new Comparison<T>("greater", a, b);
        public static Expression<bool> Less<T>(Expression<T> a, Expression<T> b) => Not(AtLeast(a,b));
        public static Expression<bool> And(params Expression<bool>[] values) => new BooleanExpression("and", values);
        public static Expression<bool> Or(params Expression<bool>[] values) => new BooleanExpression("or", values);
        public static Expression<bool> Not(Expression<bool> value) => new BooleanExpression("not", new[] { value });
        public static Expression<T> If<T>(Expression<bool> condition, Expression<T> yes, Expression<T> no) => new Conditional<T>(condition, yes, no);
        public static Expression<int> Count(TargetQuery query) => new CountExpression(query);
        public static Expression<T> Sum<T>(TargetQuery query, PropertyKey<T> property) => new SumExpression<T>(query,property);
        public static Expression<T> Custom<T>(string id) => new CustomExpression<T>(id);
        private sealed class IdentityExpression:Expression<string>
        {private readonly EntityScope scope;public IdentityExpression(EntityScope scope){this.scope=scope;}public override string Evaluate(EvaluationContext c)=>c.Resolve(scope).Id;public override IEnumerable<PropertyReference> References=>Array.Empty<PropertyReference>();}
        private sealed class IntDivide:Expression<int>
        {public override IEnumerable<string> RequiredExtensions=>a.RequiredExtensions.Concat(b.RequiredExtensions);private readonly Expression<int>a,b;public IntDivide(Expression<int>a,Expression<int>b){this.a=a;this.b=b;}public override int Evaluate(EvaluationContext c)=>a.Evaluate(c)/b.Evaluate(c);public override IEnumerable<PropertyReference> References=>a.References.Concat(b.References);}
        private sealed class CeilingExpression:Expression<double>
        {public override IEnumerable<string> RequiredExtensions=>value.RequiredExtensions;private readonly Expression<double>value;public CeilingExpression(Expression<double>value){this.value=value;}public override double Evaluate(EvaluationContext c)=>Math.Ceiling(value.Evaluate(c));public override IEnumerable<PropertyReference> References=>value.References;}
        private sealed class Literal<T> : Expression<T>
        { private readonly T value; public Literal(T value) { this.value=value; } public override T Evaluate(EvaluationContext c)=>value; public override IEnumerable<PropertyReference> References=>Array.Empty<PropertyReference>(); }
        private sealed class PropertyExpression<T> : Expression<T>
        { private readonly PropertyKey<T> key; private readonly EntityScope scope; public PropertyExpression(PropertyKey<T> key, EntityScope scope){this.key=key;this.scope=scope;} public override T Evaluate(EvaluationContext c)=>c.Read(c.Resolve(scope),key); public override IEnumerable<PropertyReference> References=>new[]{new PropertyReference(key.Name,typeof(T),scope)}; }
        private sealed class ArgumentExpression<T> : Expression<T>
        { private readonly string name; public ArgumentExpression(string name){this.name=name;} public override T Evaluate(EvaluationContext c)=>c.Arguments.TryGetValue(name,out var value)?(value is T typed?typed:(T)Convert.ChangeType(value,typeof(T),System.Globalization.CultureInfo.InvariantCulture)):throw new ArgumentException("Missing argument "+name); public override IEnumerable<PropertyReference> References=>Array.Empty<PropertyReference>(); }
        private sealed class TimeExpression : Expression<double>
        { public override double Evaluate(EvaluationContext c)=>c.Time; public override IEnumerable<PropertyReference> References=>Array.Empty<PropertyReference>(); }
        private sealed class Binary<T> : Expression<T>
        {public override IEnumerable<string> RequiredExtensions=>a.RequiredExtensions.Concat(b.RequiredExtensions); private readonly string op; private readonly Expression<T> a,b; public Binary(string op,Expression<T> a,Expression<T> b){this.op=op;this.a=a;this.b=b;} public override T Evaluate(EvaluationContext c){var x=a.Evaluate(c);var y=b.Evaluate(c);var n=Numeric<T>.Operations;return op=="add"?n.Add(x,y):op=="multiply"?n.Multiply(x,y):op=="min"?(n.Compare(x,y)<0?x:y):(n.Compare(x,y)>0?x:y);} public override IEnumerable<PropertyReference> References=>a.References.Concat(b.References); }
        private sealed class Unary<T> : Expression<T>
        {public override IEnumerable<string> RequiredExtensions=>value.RequiredExtensions; private readonly Expression<T> value; public Unary(Expression<T> value){this.value=value;} public override T Evaluate(EvaluationContext c)=>Numeric<T>.Operations.Negate(value.Evaluate(c)); public override IEnumerable<PropertyReference> References=>value.References; }
        private sealed class Comparison<T> : Expression<bool>
        {public override IEnumerable<string> RequiredExtensions=>a.RequiredExtensions.Concat(b.RequiredExtensions); private readonly string op;private readonly Expression<T>a,b;public Comparison(string op,Expression<T>a,Expression<T>b){this.op=op;this.a=a;this.b=b;}public override bool Evaluate(EvaluationContext c)=>op=="equal"?EqualityComparer<T>.Default.Equals(a.Evaluate(c),b.Evaluate(c)):Numeric<T>.Operations.Compare(a.Evaluate(c),b.Evaluate(c))>=0;public override IEnumerable<PropertyReference> References=>a.References.Concat(b.References); }
        private sealed class BooleanExpression : Expression<bool>
        {public override IEnumerable<string> RequiredExtensions=>values.SelectMany(x=>x.RequiredExtensions); private readonly string op;private readonly Expression<bool>[] values;public BooleanExpression(string op,Expression<bool>[]values){this.op=op;this.values=values;}public override bool Evaluate(EvaluationContext c)=>op=="and"?values.All(x=>x.Evaluate(c)):op=="or"?values.Any(x=>x.Evaluate(c)):!values[0].Evaluate(c);public override IEnumerable<PropertyReference> References=>values.SelectMany(x=>x.References); }
        private sealed class Conditional<T> : Expression<T>
        {public override IEnumerable<string> RequiredExtensions=>condition.RequiredExtensions.Concat(yes.RequiredExtensions).Concat(no.RequiredExtensions); private readonly Expression<bool> condition;private readonly Expression<T>yes,no;public Conditional(Expression<bool> condition,Expression<T> yes,Expression<T> no){this.condition=condition;this.yes=yes;this.no=no;}public override T Evaluate(EvaluationContext c)=>condition.Evaluate(c)?yes.Evaluate(c):no.Evaluate(c);public override IEnumerable<PropertyReference> References=>condition.References.Concat(yes.References).Concat(no.References); }
        private sealed class DoubleBinary : Expression<double>
        {public override IEnumerable<string> RequiredExtensions=>a.RequiredExtensions.Concat(b.RequiredExtensions);private readonly string op;private readonly Expression<double>a,b;public DoubleBinary(string op,Expression<double>a,Expression<double>b){this.op=op;this.a=a;this.b=b;}public override double Evaluate(EvaluationContext c)=>op=="divide"?a.Evaluate(c)/b.Evaluate(c):Math.Pow(a.Evaluate(c),b.Evaluate(c));public override IEnumerable<PropertyReference> References=>a.References.Concat(b.References);}
        private sealed class ModuloExpression : Expression<int>
        {public override IEnumerable<string> RequiredExtensions=>a.RequiredExtensions.Concat(b.RequiredExtensions);private readonly Expression<int>a,b;public ModuloExpression(Expression<int>a,Expression<int>b){this.a=a;this.b=b;}public override int Evaluate(EvaluationContext c)=>a.Evaluate(c)%b.Evaluate(c);public override IEnumerable<PropertyReference> References=>a.References.Concat(b.References);}
        private sealed class ConvertExpression : Expression<double>
        {public override IEnumerable<string> RequiredExtensions=>value.RequiredExtensions;private readonly Expression<int> value;public ConvertExpression(Expression<int>value){this.value=value;}public override double Evaluate(EvaluationContext c)=>value.Evaluate(c);public override IEnumerable<PropertyReference> References=>value.References;}
        private sealed class CountExpression : Expression<int>
        {public override IEnumerable<string> RequiredExtensions=>query.RequiredExtensions;private readonly TargetQuery query;public CountExpression(TargetQuery query){this.query=query;}public override int Evaluate(EvaluationContext c)=>query.Select(c).Count;public override IEnumerable<PropertyReference> References=>query.References;}
        private sealed class SumExpression<T> : Expression<T>
        {public override IEnumerable<string> RequiredExtensions=>query.RequiredExtensions;private readonly TargetQuery query;private readonly PropertyKey<T> key;public SumExpression(TargetQuery q,PropertyKey<T>key){query=q;this.key=key;}public override T Evaluate(EvaluationContext c){var n=Numeric<T>.Operations;T result=n.Zero;foreach(var entity in query.Select(c))result=n.Add(result,c.Read(entity,key));return result;}public override IEnumerable<PropertyReference> References=>query.References.Concat(new[]{new PropertyReference(key.Name,typeof(T),EntityScope.Target)});}
        private sealed class CustomExpression<T> : Expression<T>
        {public override IEnumerable<string> RequiredExtensions=>new[]{Id};internal readonly string Id;public CustomExpression(string id){Id=id;}public override T Evaluate(EvaluationContext c)=>c.Extensions.Calculate<T>(Id,c.ReadOnly());public override IEnumerable<PropertyReference> References=>Array.Empty<PropertyReference>();}
    }

    public class ReadOnlyEvaluationContext
    {
        protected readonly EvaluationContext context;
        internal ReadOnlyEvaluationContext(EvaluationContext context){this.context=context;}
        public string ActorId=>context.Self.Id;
        public string TargetId=>context.Target.Id;
        public double Time=>context.Time;
        public T Read<T>(string entityId,PropertyKey<T> key)=>(entityId == context.Self.Id ? context.Self : context.World.Find(entityId)).GetValue(key).Current;
        public IReadOnlyList<string> EntityIds=>context.World.Entities.Keys.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    }
    public interface ICalculation<T>{T Evaluate(ReadOnlyEvaluationContext context);}
    public interface ITargetSelector{IEnumerable<string> Select(ReadOnlyEvaluationContext context);}
    public interface IEffectHandler{void Stage(EffectExecutionContext context,EntityTransaction transaction);}
    public sealed class EffectExecutionContext:ReadOnlyEvaluationContext
    {
        internal EffectExecutionContext(EvaluationContext context):base(context){ }
        public void Set<T>(EntityTransaction transaction,string entityId,PropertyKey<T> key,T value)=>transaction.Set(context.World.Find(entityId).GetValue(key),value);
        public void Remove(EntityTransaction transaction,string entityId)=>transaction.Remove(context.World.Find(entityId));
        public ulong NextRandom()=>context.World.NextRandomUInt64();
    }
    public sealed class ExtensionRegistry
    {
        private readonly Dictionary<string,object> hooks=new Dictionary<string,object>(StringComparer.Ordinal);
        public ExtensionRegistry Register<T>(string id,T handler) where T:class {if(string.IsNullOrWhiteSpace(id)||handler==null||hooks.ContainsKey(id))throw new ArgumentException("Invalid or duplicate extension "+id);hooks.Add(id,handler);return this;}
        public bool Contains(string id)=>hooks.ContainsKey(id);
        public T Get<T>(string id) where T:class=>hooks.TryGetValue(id,out var hook)&&hook is T typed?typed:throw new ArgumentException("Missing or incompatible extension "+id);
        public T Calculate<T>(string id,ReadOnlyEvaluationContext context)=>Get<ICalculation<T>>(id).Evaluate(context);
    }
    public static class EvaluationExtensions
    {public static ReadOnlyEvaluationContext ReadOnly(this EvaluationContext context)=>new ReadOnlyEvaluationContext(context);
        public static EffectExecutionContext EffectsContext(this EvaluationContext context)=>new EffectExecutionContext(context);}
}
