using System.Collections.Generic;
using BreakInfinity;
using IncrementalFramework.Definitions;

namespace IncrementalFramework.Economy
{
    public static class EconomyExpressions
    {
        static EconomyExpressions(){BigDoubleNumeric.Register();}
        public static Expression<BigDouble> Big(Expression<double> value)=>new BigExpression(value);
        private sealed class BigExpression:Expression<BigDouble>
        {public override IEnumerable<string> RequiredExtensions=>source.RequiredExtensions;private readonly Expression<double> source;public BigExpression(Expression<double> source){this.source=source;}public override BigDouble Evaluate(EvaluationContext context)=>new BigDouble(source.Evaluate(context));public override IEnumerable<PropertyReference> References=>source.References;}
    }
}
