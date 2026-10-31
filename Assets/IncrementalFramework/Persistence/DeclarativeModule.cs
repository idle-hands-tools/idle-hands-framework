using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.Progression;

namespace IncrementalFramework.Persistence
{
    public sealed class DeclarativeModule:IGameModule
    {
        private readonly CompiledGameDefinition definition;
        private readonly string sectionId;
        public DeclarativeRuntime Runtime{get;private set;}
        public DeclarativeModule(CompiledGameDefinition definition,string sectionId="declared-behaviors"){this.definition=definition;this.sectionId=sectionId;}
        public void Register(ModuleBuilder builder)
        {
            foreach(var template in definition.Templates.Values)builder.Entity(template);
            builder.Configure(simulation=>Runtime=new DeclarativeRuntime(simulation,definition));
            builder.Snapshot(new BehaviorSnapshotSection(sectionId,()=>Runtime.Behaviors));
        }
    }
}
