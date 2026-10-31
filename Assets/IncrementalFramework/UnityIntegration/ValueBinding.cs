using System;
using System.Collections.Generic;
using IncrementalFramework.Core;

namespace IncrementalFramework.Unity
{
    /// <summary>Refreshes a view only when a Value read by that view changes.</summary>
    public sealed class ValueBinding:IDisposable
    {
        private readonly EntityContext context;
        private readonly Action update;
        private HashSet<Value> dependencies=new HashSet<Value>();
        private bool disposed;
        public bool HasDependencies=>dependencies.Count>0;
        public bool Timed{get;}
        public ValueBinding(EntityContext context,Action update,bool timed=false)
        {this.context=context;this.update=update;Timed=timed;Refresh();context.ValueChanged+=OnChanged;}
        private void OnChanged(Value value,object before,object after){if(dependencies.Contains(value))Refresh();}
        public void Refresh(){if(!disposed)dependencies=new HashSet<Value>(context.ObserveReads(update));}
        public void Dispose(){if(disposed)return;disposed=true;context.ValueChanged-=OnChanged;dependencies.Clear();}
    }
}
