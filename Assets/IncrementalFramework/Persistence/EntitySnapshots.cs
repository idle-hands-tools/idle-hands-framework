using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IncrementalFramework.Persistence
{
    public sealed class EntityRecord
    {
        public string Id{get;set;}
        public string TemplateId{get;set;}
        public Dictionary<string,JToken> Properties{get;set;}=new Dictionary<string,JToken>();
    }
    public sealed class ModifierRecord
    {
        public string Id{get;set;}public string Source{get;set;}public string Target{get;set;}public string Property{get;set;}
        public NumericOperation Operation{get;set;}public int Priority{get;set;}public JToken Amount{get;set;}
    }
    public sealed class EntityWorldSnapshot
    {
        public long NextIdentity{get;set;}
        public ulong RandomState{get;set;}=1;
        public List<EntityRecord> Entities{get;set;}=new List<EntityRecord>();
        public List<ModifierRecord> Modifiers{get;set;}=new List<ModifierRecord>();
    }
    public static class EntitySnapshots
    {
        private static object Decode(JToken token,Type type,JsonSerializer serializer)
        {return Nullable.GetUnderlyingType(type)==typeof(BigDouble)?(token.Type==JTokenType.Null?null:(object)token.ToObject<BigDouble>(serializer)):token.ToObject(type,serializer);}
        public static EntityWorldSnapshot Capture(EntityContext context)
        {
            var serializer=JsonSerializer.Create(SnapshotService.CreateSettings());
            var result=new EntityWorldSnapshot{NextIdentity=context.NextIdentitySequence,RandomState=context.RandomState};
            foreach(var entity in context.Entities.Values.OrderBy(x=>x.Id,StringComparer.Ordinal))
            {
                var record=new EntityRecord{Id=entity.Id,TemplateId=entity.TemplateId};
                foreach(var property in entity.Values.OrderBy(x=>x.Key,StringComparer.Ordinal))
                    if(!property.Value.HasFormula)record.Properties.Add(property.Key,property.Value.BaseBoxed==null?JValue.CreateNull():JToken.FromObject(property.Value.BaseBoxed,serializer));
                result.Entities.Add(record);
            }
            foreach(var modifier in context.Modifiers)
                result.Modifiers.Add(new ModifierRecord{Id=modifier.Id,Source=modifier.SourceId,Target=modifier.Target.Id,Property=modifier.Property,Operation=modifier.Operation,Priority=modifier.Priority,Amount=JToken.FromObject(modifier.Operand,serializer)});
            return result;
        }
        public static void Restore(EntityContext context,EntityWorldSnapshot snapshot)
        {
            if(snapshot==null||snapshot.NextIdentity<1||snapshot.Entities==null||snapshot.Modifiers==null||snapshot.Entities.Count>100000)throw new ArgumentException("Invalid entity snapshot.");
            if(snapshot.Entities.Any(x=>x==null||string.IsNullOrWhiteSpace(x.Id)||string.IsNullOrWhiteSpace(x.TemplateId)||x.Properties==null)||snapshot.Entities.Select(x=>x.Id).Distinct().Count()!=snapshot.Entities.Count)throw new ArgumentException("Invalid entity identity.");
            var serializer=JsonSerializer.Create(SnapshotService.CreateSettings());
            // Validate templates and property schemas before touching the destination world.
            var schemas=new Dictionary<string,Entity>();
            foreach(var record in snapshot.Entities)
            {
                if(context.Entities.TryGetValue(record.Id,out var existing))schemas.Add(record.Id,existing);
                else if(context.Templates.TryGetValue(record.TemplateId,out var template))schemas.Add(record.Id,template.Instantiate(new EntityContext(),record.Id));
                else throw new ArgumentException("Missing entity template "+record.TemplateId);
                foreach(var property in record.Properties)
                {
                    var value=schemas[record.Id].GetValue(property.Key);
                    if(value.HasFormula)throw new ArgumentException("Calculated property cannot be restored: "+property.Key);
                    _=Decode(property.Value,value.DataType,serializer);
                }
            }
            foreach(var modifier in snapshot.Modifiers)
            {
                if(!schemas.TryGetValue(modifier.Target,out var target)||!Enum.IsDefined(typeof(NumericOperation),modifier.Operation))throw new ArgumentException("Invalid modifier target.");
                var type=target.GetValue(modifier.Property).DataType;
                if(type!=typeof(int)&&type!=typeof(long)&&type!=typeof(double)&&type!=typeof(BigDouble))throw new ArgumentException("Unsupported numeric modifier type.");
                _=modifier.Amount.ToObject(type,serializer);
            }
            using(context.RestoreScope())
            {
                foreach(var source in context.Modifiers.Select(x=>x.SourceId).Distinct().ToArray())context.RemoveModifiers(source);
                foreach(var record in snapshot.Entities)
                    if(!context.Entities.ContainsKey(record.Id))context.Templates[record.TemplateId].Instantiate(context,record.Id);
                using(context.Batch())
                    foreach(var record in snapshot.Entities)
                        foreach(var property in record.Properties)
                        {var value=context.Find(record.Id).GetValue(property.Key);value.Boxed=Decode(property.Value,value.DataType,serializer);}
                foreach(var modifier in snapshot.Modifiers)
                {
                    var target=context.Find(modifier.Target);var type=target.GetValue(modifier.Property).DataType;var tx=new EntityTransaction();
                    if(type==typeof(int))new NumericModifier<int>(modifier.Id,modifier.Source,new PropertyKey<int>(modifier.Property),modifier.Operation,modifier.Amount.ToObject<int>(serializer),modifier.Priority).Stage(target,tx);
                    else if(type==typeof(long))new NumericModifier<long>(modifier.Id,modifier.Source,new PropertyKey<long>(modifier.Property),modifier.Operation,modifier.Amount.ToObject<long>(serializer),modifier.Priority).Stage(target,tx);
                    else if(type==typeof(double))new NumericModifier<double>(modifier.Id,modifier.Source,new PropertyKey<double>(modifier.Property),modifier.Operation,modifier.Amount.ToObject<double>(serializer),modifier.Priority).Stage(target,tx);
                    else new NumericModifier<BigDouble>(modifier.Id,modifier.Source,new PropertyKey<BigDouble>(modifier.Property),modifier.Operation,modifier.Amount.ToObject<BigDouble>(serializer),modifier.Priority).Stage(target,tx);
                    if(!tx.Commit())throw new ArgumentException("Invalid modifier binding.");
                }
                context.NextIdentitySequence=snapshot.NextIdentity;context.RandomState=snapshot.RandomState;
                foreach(var entity in context.Entities.Values)foreach(var value in entity.Values.Values)if(value.IsCalculated)value.Recalculate();
            }
        }
    }
    public sealed class BehaviorSnapshotSection:ISnapshotSection
    {
        private sealed class Parameter { public string Type; public JToken Data; }
        private sealed class Snapshot
        {
            public double Time; public long NextSequence; public List<BehaviorJob> Jobs;
            public Dictionary<string,Dictionary<string,Parameter>> Parameters=new Dictionary<string,Dictionary<string,Parameter>>();
            public Dictionary<string,string> Programs=new Dictionary<string,string>();
        }
        private readonly Func<BehaviorRunner> getRunner;
        private BehaviorRunner runner=>getRunner();
        public string Id{get;}
        public int Version=>1;
        public BehaviorSnapshotSection(string id,BehaviorRunner runner):this(id,()=>runner){}
        public BehaviorSnapshotSection(string id,Func<BehaviorRunner> runner){Id=id;getRunner=runner;}
        private static Parameter Encode(object value,JsonSerializer serializer)
        {
            string type=value==null?"null":value is string?"string":value is bool?"bool":value is int?"i32":value is long?"i64":value is double?"f64":value is BigDouble?"big":value is string[]?"strings":null;
            if(type==null)throw new ArgumentException("Behavior arguments must be scalar values, entity IDs, or string arrays; store custom state in entity Values.");
            return new Parameter{Type=type,Data=value==null?JValue.CreateNull():JToken.FromObject(value,serializer)};
        }
        private static object Decode(Parameter parameter,JsonSerializer serializer)
        {
            if(parameter==null||parameter.Data==null)throw new ArgumentException("Invalid behavior argument.");
            switch(parameter.Type)
            {
                case "null":return null;case "string":return parameter.Data.ToObject<string>(serializer);case "bool":return parameter.Data.ToObject<bool>(serializer);
                case "i32":return parameter.Data.ToObject<int>(serializer);case "i64":return parameter.Data.ToObject<long>(serializer);case "f64":return parameter.Data.ToObject<double>(serializer);
                case "big":return parameter.Data.ToObject<BigDouble>(serializer);case "strings":return parameter.Data.ToObject<string[]>(serializer);
                default:throw new ArgumentException("Unknown behavior argument kind.");
            }
        }
        public string Capture()
        {
            var serializer=JsonSerializer.Create(SnapshotService.CreateSettings());
            var snapshot=new Snapshot{Time=runner.Time,NextSequence=runner.NextSequence,Jobs=new List<BehaviorJob>()};
            foreach(var job in runner.Jobs)
            {
                snapshot.Jobs.Add(new BehaviorJob{Sequence=job.Sequence,BehaviorId=job.BehaviorId,ActorId=job.ActorId,TargetId=job.TargetId,SourceId=job.SourceId,ProgramCounter=job.ProgramCounter,WakeTime=job.WakeTime,Error=job.Error,Loops=job.Loops,Arguments=new Dictionary<string,object>()});
                snapshot.Parameters.Add(job.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),job.Arguments.ToDictionary(x=>x.Key,x=>Encode(x.Value,serializer)));
                snapshot.Programs[job.BehaviorId]=runner.ProgramSignatures[job.BehaviorId];
            }
            return JsonConvert.SerializeObject(snapshot,SnapshotService.CreateSettings());
        }
        private Snapshot Read(int version,string data)
        {
            if(version!=Version)throw new ArgumentException("Unsupported behavior version.");
            var snapshot=JsonConvert.DeserializeObject<Snapshot>(data,SnapshotService.CreateSettings());
            if(snapshot==null||snapshot.Jobs==null||snapshot.Parameters==null||snapshot.Programs==null)throw new ArgumentException("Invalid behavior state.");
            var serializer=JsonSerializer.Create(SnapshotService.CreateSettings());
            foreach(var job in snapshot.Jobs)
            {
                if(!snapshot.Parameters.TryGetValue(job.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),out var parameters))throw new ArgumentException("Missing behavior parameters.");
                job.Arguments=parameters.ToDictionary(x=>x.Key,x=>Decode(x.Value,serializer));
            }
            return snapshot;
        }
        public void Validate(int version,string data){_=Read(version,data);}
        public void Restore(int version,string data)
        {
            var snapshot=Read(version,data);
            foreach(var program in snapshot.Programs)
                if(!runner.ProgramSignatures.TryGetValue(program.Key,out var signature)||signature!=program.Value)throw new ArgumentException("Behavior program changed: "+program.Key);
            runner.Restore(snapshot.Time,snapshot.NextSequence,snapshot.Jobs);
        }
    }
}
