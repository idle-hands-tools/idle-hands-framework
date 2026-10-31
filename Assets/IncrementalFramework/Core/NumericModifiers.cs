using System;
using System.Collections.Generic;
using System.Linq;

namespace IncrementalFramework.Core
{
    public enum NumericOperation { Flat, AddPercent, Multiply, Override }
    public interface INumeric<T>
    {
        T Zero { get; }
        T One { get; }
        T Add(T left, T right);
        T Multiply(T left, T right);
        T Negate(T value);
        int Compare(T left, T right);
    }
    public static class Numeric<T>
    {
        private static INumeric<T> operations;
        public static INumeric<T> Operations
        {
            get
            {
                if (operations != null) return operations;
                if (typeof(T) == typeof(double)) return operations = (INumeric<T>)(object)new DoubleNumeric();
                if (typeof(T) == typeof(int)) return operations = (INumeric<T>)(object)new IntNumeric();
                if (typeof(T) == typeof(long)) return operations = (INumeric<T>)(object)new LongNumeric();
                throw new InvalidOperationException("No numeric adapter registered for " + typeof(T).FullName);
            }
            set => operations = value ?? throw new ArgumentNullException(nameof(value));
        }
    }
    internal sealed class DoubleNumeric : INumeric<double>
    { public double Zero => 0; public double One => 1; public double Add(double a, double b) => a + b; public double Multiply(double a, double b) => a * b; public double Negate(double a) => -a; public int Compare(double a, double b) => a.CompareTo(b); }
    internal sealed class IntNumeric : INumeric<int>
    { public int Zero => 0; public int One => 1; public int Add(int a, int b) => checked(a + b); public int Multiply(int a, int b) => checked(a * b); public int Negate(int a) => checked(-a); public int Compare(int a, int b) => a.CompareTo(b); }
    internal sealed class LongNumeric : INumeric<long>
    { public long Zero => 0; public long One => 1; public long Add(long a, long b) => checked(a + b); public long Multiply(long a, long b) => checked(a * b); public long Negate(long a) => checked(-a); public int Compare(long a, long b) => a.CompareTo(b); }

    public abstract class ModifierApplication
    {
        public string Id { get; }
        public string SourceId { get; }
        public EntityInstance Target { get; }
        public string Property { get; }
        public NumericOperation Operation { get; }
        public int Priority { get; }
        public abstract object Operand { get; }
        protected ModifierApplication(string id, string source, EntityInstance target, string property, NumericOperation operation, int priority)
        { Id = id; SourceId = source; Target = target; Property = property; Operation = operation; Priority = priority; }
        internal abstract void Rebuild(IEnumerable<ModifierApplication> applications);
    }
    public sealed class NumericModifier<T> : Modifier
    {
        public string SourceId { get; }
        public PropertyKey<T> Property { get; }
        public NumericOperation Operation { get; }
        public T Amount { get; }
        public int Priority { get; }
        public NumericModifier(string id, string source, PropertyKey<T> property, NumericOperation operation, T amount, int priority = 0) : base(id)
        { SourceId = source; Property = property; Operation = operation; Amount = amount; Priority = priority; }
        public override void Stage(EntityInstance target, EntityTransaction transaction)
        {
            _ = target.GetValue(Property);
            var binding = new Application(Id, SourceId, target, Property.Name, Operation, Amount, Priority);
            transaction.Attach(binding);
        }
        private sealed class Application : ModifierApplication
        {
            private readonly T amount;
            public override object Operand => amount;
            public Application(string id, string source, EntityInstance target, string property, NumericOperation operation, T value, int priority)
                : base(id, source, target, property, operation, priority) { amount = value; }
            internal override void Rebuild(IEnumerable<ModifierApplication> applications)
            {
                var group = applications.Where(x => ReferenceEquals(x.Target, Target) && x.Property == Property).ToArray();
                var value = Target.GetValue<T>(Property);
                value.Transform = group.Length == 0 ? null : new Func<T, T>(input =>
                {
                    var numeric = Numeric<T>.Operations;
                    T flat = numeric.Zero, percent = numeric.Zero, product = numeric.One;
                    ModifierApplication replacement = null;
                    foreach (var modifier in group)
                        switch (modifier.Operation)
                        {
                            case NumericOperation.Flat: flat = numeric.Add(flat, (T)modifier.Operand); break;
                            case NumericOperation.AddPercent: percent = numeric.Add(percent, (T)modifier.Operand); break;
                            case NumericOperation.Multiply: product = numeric.Multiply(product, (T)modifier.Operand); break;
                            case NumericOperation.Override:
                                if (replacement == null || modifier.Priority > replacement.Priority ||
                                    (modifier.Priority == replacement.Priority && string.CompareOrdinal(modifier.Id, replacement.Id) > 0)) replacement = modifier;
                                break;
                        }
                    return replacement != null ? (T)replacement.Operand : numeric.Multiply(numeric.Multiply(numeric.Add(input, flat), numeric.Add(numeric.One, percent)), product);
                });
                value.Recalculate();
            }
        }
    }
}
