using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;

namespace IncrementalFramework.UI
{
    public sealed class UiContext
    {
        public EntityContext World { get; }
        public EntityInstance Root { get; }
        public EntityInstance Item { get; }
        public Func<double> Clock { get; }
        public ExtensionRegistry Extensions { get; }
        public Action<string> Diagnostic { get; }
        public Action<string> ActionRejected { get; }
        public IUiPresentationPolicy Presentation { get; }
        public UiContext(EntityContext world, EntityInstance root = null, Func<double> clock = null,
            ExtensionRegistry extensions = null, Action<string> diagnostic = null, Action<string> actionRejected = null, IUiPresentationPolicy presentation = null)
            : this(world, root, root, clock ?? (() => 0), extensions ?? new ExtensionRegistry(), diagnostic, actionRejected, presentation) { }
        private UiContext(EntityContext world, EntityInstance root, EntityInstance item, Func<double> clock,
            ExtensionRegistry extensions, Action<string> diagnostic, Action<string> rejected, IUiPresentationPolicy presentation)
        { World = world ?? throw new ArgumentNullException(nameof(world)); Root = root; Item = item; Clock = clock; Extensions = extensions; Diagnostic = diagnostic; ActionRejected = rejected; Presentation = presentation; }
        public UiContext ForItem(EntityInstance item) => new UiContext(World, Root, item, Clock, Extensions, Diagnostic, ActionRejected, Presentation);
        public EvaluationContext Evaluation()
        {
            var context = new EvaluationContext(Item ?? Root ?? throw new InvalidOperationException("An expression binding requires a root entity."), Extensions);
            context.Time = Clock(); return context;
        }
    }
    public interface IUiBinding { object ReadBoxed(UiContext context); double RefreshInterval { get; } }
    public sealed class UiBinding<T> : IUiBinding
    {
        private readonly Func<UiContext, T> read;
        public double RefreshInterval { get; }
        private UiBinding(Func<UiContext,T> read, double interval)
        { this.read = read ?? throw new ArgumentNullException(nameof(read)); if (interval < 0 || double.IsNaN(interval) || double.IsInfinity(interval)) throw new ArgumentOutOfRangeException(nameof(interval)); RefreshInterval = interval; }
        public T Read(UiContext context) => read(context);
        public object ReadBoxed(UiContext context) => Read(context);
        public static UiBinding<T> Constant(T value) => new UiBinding<T>(_ => value, 0);
        public static UiBinding<T> From(Func<UiContext,T> read, double refreshInterval = 0) => new UiBinding<T>(read, refreshInterval);
        public static UiBinding<T> From(Expression<T> expression, double refreshInterval = 0) => From(c => expression.Evaluate(c.Evaluation()), refreshInterval);
        public static UiBinding<T> From(Value<T> value) => From(_ => value.Current);
        public static implicit operator UiBinding<T>(T value) => Constant(value);
    }
    public readonly struct TextArgument
    {
        public string Name { get; }
        public IUiBinding Binding { get; }
        public TextArgument(string name, IUiBinding binding) { Name = name; Binding = binding; }
    }
    /// <summary>Named placeholders with invariant numeric formats. Parsing never evaluates game state.</summary>
    public sealed class TextTemplate
    {
        private readonly string template;
        private readonly Dictionary<string, IUiBinding> arguments;
        private readonly List<(string Text, string Name, string Format)> parts = new List<(string,string,string)>();
        public double RefreshInterval => arguments.Count == 0 ? 0 : arguments.Values.Where(x => x.RefreshInterval > 0).Select(x => x.RefreshInterval).DefaultIfEmpty(0).Min();
        public TextTemplate(string template, params TextArgument[] bindings)
        {
            this.template = template ?? throw new ArgumentNullException(nameof(template));
            arguments = new Dictionary<string,IUiBinding>(StringComparer.Ordinal);
            foreach (var binding in bindings ?? Array.Empty<TextArgument>())
                if (string.IsNullOrWhiteSpace(binding.Name) || binding.Binding == null || arguments.ContainsKey(binding.Name)) throw new ArgumentException("Invalid or duplicate text binding.");
                else arguments.Add(binding.Name, binding.Binding);
            Parse();
        }
        public static TextArgument Bind<T>(string name, UiBinding<T> value) => new TextArgument(name, value);
        public static TextTemplate Create(string template, params TextArgument[] bindings) => new TextTemplate(template, bindings);
        public static TextTemplate Read(Func<UiContext,string> reader, double refreshInterval = 0) => new TextTemplate("{text}", Bind("text", UiBinding<string>.From(reader,refreshInterval)));
        public static TextTemplate Literal(string value) => new TextTemplate((value ?? "").Replace("{","{{").Replace("}","}}"));
        public static implicit operator TextTemplate(string value) => Literal(value);
        private void Parse()
        {
            var literal = new StringBuilder();
            for (int i=0;i<template.Length;)
            {
                char ch=template[i++];
                if ((ch=='{' || ch=='}') && i<template.Length && template[i]==ch) {literal.Append(ch);i++;continue;}
                if(ch=='}')throw new FormatException("Unescaped closing brace in text template.");
                if(ch!='{'){literal.Append(ch);continue;}
                if(literal.Length>0){parts.Add((literal.ToString(),null,null));literal.Clear();}
                int end=template.IndexOf('}',i);if(end<0)throw new FormatException("Unclosed text placeholder.");
                string field=template.Substring(i,end-i);int colon=field.IndexOf(':');string name=colon<0?field:field.Substring(0,colon);
                if(!Regex.IsMatch(name,@"^[A-Za-z_][A-Za-z0-9_]*$") || !arguments.ContainsKey(name))throw new FormatException("Missing or invalid text binding: "+name);
                parts.Add((null,name,colon<0?null:field.Substring(colon+1)));i=end+1;
            }
            if(literal.Length>0)parts.Add((literal.ToString(),null,null));
        }
        private static string NormalizeZero(string text)
        {return text.StartsWith("-",StringComparison.Ordinal)&&text.Length>1&&text.Skip(1).All(c=>c=='0'||c=='.'||c==',')?text.Substring(1):text;}
        public string Evaluate(UiContext context)
        {
            var result=new StringBuilder();
            foreach(var part in parts)
            {
                if(part.Name==null){result.Append(part.Text);continue;}
                object value=arguments[part.Name].ReadBoxed(context);
                result.Append(CompactNumberText.IsCompact(part.Format) ? CompactNumberText.Format(value,part.Format)
                    : value is BigDouble big ? BigNumberText.Format(big,part.Format)
                    : value is IFormattable formatted ? NormalizeZero(formatted.ToString(part.Format,CultureInfo.InvariantCulture)) : value?.ToString() ?? "");
            }
            return result.ToString();
        }
    }
    internal static class CompactNumberText
    {
        private static readonly string[] Suffixes={"","k","M","B","T","Qa","Qi","Sx","Sp","Oc","No"};
        public static bool IsCompact(string format)=>format!=null&&format.StartsWith("compact",StringComparison.OrdinalIgnoreCase);
        public static string Format(object value,string format)
        {
            string digits;
            if(string.Equals(format,"compact",StringComparison.OrdinalIgnoreCase))digits="0.#";
            else if(format.StartsWith("compact:",StringComparison.OrdinalIgnoreCase))digits=format.Substring(8);
            else throw new FormatException("Use compact or compact:0.## for compact numbers.");
            if(!Regex.IsMatch(digits,@"^[#0,]+(?:\.0*#*)?$"))throw new FormatException("Compact precision requires a decimal pattern such as 0.# or 0.00.");
            BigDouble number;
            if(value is BigDouble big)number=big;
            else
            {
                switch(Type.GetTypeCode(value?.GetType()))
                {
                    case TypeCode.Byte:case TypeCode.SByte:case TypeCode.Int16:case TypeCode.UInt16:
                    case TypeCode.Int32:case TypeCode.UInt32:case TypeCode.Int64:case TypeCode.UInt64:
                    case TypeCode.Single:case TypeCode.Double:case TypeCode.Decimal:
                        number=new BigDouble(Convert.ToDouble(value,CultureInfo.InvariantCulture));break;
                    default:throw new FormatException("Compact formatting requires a numeric binding.");
                }
            }
            if(double.IsNaN(number.Mantissa)||double.IsInfinity(number.Mantissa))return number.Mantissa.ToString(CultureInfo.InvariantCulture);
            // Scale only the exponent: even currencies beyond double's range stay finite.
            long exponent=number.Mantissa==0||number.Exponent<3?0:number.Exponent-number.Exponent%3;
            string coefficient=BigNumberText.Format(new BigDouble(number.Mantissa,number.Exponent-exponent),digits);
            if(double.TryParse(coefficient,NumberStyles.Float|NumberStyles.AllowThousands,CultureInfo.InvariantCulture,out var rounded)
                &&Math.Abs(rounded)>=1000&&exponent<=long.MaxValue-3)
            {
                exponent+=3;
                coefficient=BigNumberText.Format(new BigDouble(number.Mantissa,number.Exponent-exponent),digits);
            }
            long group=exponent/3;
            return coefficient+(group<Suffixes.Length?Suffixes[(int)group]:"e"+exponent.ToString(CultureInfo.InvariantCulture));
        }
    }
    internal static class BigNumberText
    {
        // Work from normalized mantissa/exponent, never narrow a BigDouble to double.
        public static string Format(BigDouble value,string format)
        {
            if(string.IsNullOrEmpty(format))format="G";
            var match=Regex.Match(format,@"^(?<integer>[#0,]+)(?:\.(?<fraction>0*#*))?$");
            if(!match.Success)
            {
                // The vendor supports standard G/E/F formats; reject unsupported custom syntax explicitly.
                if(!Regex.IsMatch(format,@"^[gGeEfF]\d*$"))throw new FormatException("Unsupported BigDouble format: "+format);
                char kind=char.ToUpperInvariant(format[0]);int places=format.Length==1?(kind=='G'?15:kind=='F'?2:6):int.Parse(format.Substring(1),CultureInfo.InvariantCulture);
                if(places>99)throw new FormatException("At most 99 digits are supported.");
                if(kind=='F')return Format(value,"0"+(places==0?"":"."+new string('0',places)));
                if(kind=='G'&&value.Exponent>=-6&&value.Exponent<21)
                {int fractionDigits=Math.Max(0,places-1-(int)value.Exponent);return Format(value,"0"+(fractionDigits==0?"":"."+new string('#',fractionDigits)));}
                return value.Mantissa.ToString(kind=='E'?"F"+places:"G"+Math.Max(1,places),CultureInfo.InvariantCulture)+"E"+(value.Exponent>=0?"+":"")+value.Exponent.ToString(CultureInfo.InvariantCulture);
            }
            if(double.IsNaN(value.Mantissa)||double.IsInfinity(value.Mantissa))return value.ToString();
            string fraction=match.Groups["fraction"].Value;int decimals=fraction.Length;
            if(decimals>99)throw new FormatException("At most 99 fractional digits are supported.");
            long exponent=value.Exponent;
            if(exponent>4096)throw new FormatException("Fixed text exceeds 4096 integer digits; use G or E format.");
            // Mantissa precision is the precision of the source type (about 15 significant digits).
            string scientific=Math.Abs(value.Mantissa).ToString("E14",CultureInfo.InvariantCulture);
            int marker=scientific.IndexOf('E');string digits=scientific.Substring(0,marker).Replace(".","");
            exponent+=int.Parse(scientific.Substring(marker+1),CultureInfo.InvariantCulture);
            int integerPlaces=exponent < -10000 ? -10000 : (int)exponent+1;
            int retained=integerPlaces+decimals;
            string scaled;
            if(value.Mantissa==0 || retained<0)scaled="0";
            else if(retained==0)scaled=digits[0]>='5'?"1":"0";
            else
            {
                scaled=retained>=digits.Length?digits.PadRight(retained,'0'):digits.Substring(0,retained);
                if(retained<digits.Length && digits[retained]>='5')
                {var chars=scaled.ToCharArray();int i=chars.Length-1;while(i>=0&&chars[i]=='9'){chars[i]='0';i--;}if(i<0)scaled="1"+new string(chars);else{chars[i]++;scaled=new string(chars);}}
            }
            scaled=scaled.TrimStart('0');if(scaled.Length==0)scaled="0";
            scaled=scaled.PadLeft(decimals+1,'0');
            string whole=scaled.Substring(0,scaled.Length-decimals),tail=decimals==0?"":scaled.Substring(scaled.Length-decimals);
            int minimum=match.Groups["integer"].Value.Count(c=>c=='0');whole=whole.PadLeft(minimum,'0');
            if(minimum==0 && whole=="0")whole="";
            if(format.Contains(","))for(int i=whole.Length-3;i>0;i-=3)whole=whole.Insert(i,",");
            int required=fraction.Count(c=>c=='0');while(tail.Length>required && tail.EndsWith("0",StringComparison.Ordinal))tail=tail.Substring(0,tail.Length-1);
            bool nonzero=scaled.Any(c=>c!='0');
            return (value.Mantissa<0&&nonzero?"-":"")+whole+(tail.Length>0?"."+tail:"");
        }
    }
}
