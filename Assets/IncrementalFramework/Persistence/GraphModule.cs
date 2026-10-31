using System;
using System.Collections.Generic;
using IncrementalFramework.Core;
using IncrementalFramework.Persistence;
using Newtonsoft.Json;

namespace IncrementalFramework.Progression.Graphs
{
    /// <summary>Composition facade in the Persistence assembly; GraphRuntime itself has no JSON dependency.</summary>
    public sealed class GraphModule : IGameModule, ISnapshotSection
    {
        public GraphRuntime Runtime { get; }
        public string Id=>Runtime.Id;
        public int Version=>1;
        public GraphModule(IEnumerable<GraphDefinition> definitions,GraphExtensions extensions=null,string id="framework.graphs")
        {Runtime=new GraphRuntime(definitions,extensions,id);}
        public void Register(ModuleBuilder builder){Runtime.Register(builder);builder.Snapshot(this);}
        public string Capture()=>JsonConvert.SerializeObject(Runtime.Export(),SnapshotService.CreateSettings());
        private GraphSnapshot Decode(int version,string data)
        {
            if(version!=Version||string.IsNullOrWhiteSpace(data)||data.Length>SnapshotService.MaximumSnapshotBytes)throw new ArgumentException("Unsupported or oversized graph snapshot.");
            var snapshot=JsonConvert.DeserializeObject<GraphSnapshot>(data,SnapshotService.CreateSettings());Runtime.ValidateSnapshot(snapshot);return snapshot;
        }
        public void Validate(int version,string data)=>Decode(version,data);
        public void Restore(int version,string data)=>Runtime.Import(Decode(version,data));
    }
}
