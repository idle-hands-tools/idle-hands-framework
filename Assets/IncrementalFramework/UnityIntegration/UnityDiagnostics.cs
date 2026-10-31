using System;
using IncrementalFramework.Core;
using UnityEngine;

namespace IncrementalFramework.Unity
{
    public sealed class UnityDiagnostics:IDisposable
    {
        private readonly EntityContext context;
        public UnityDiagnostics(EntityContext context){this.context=context;context.Diagnostic+=OnDiagnostic;}
        private static void OnDiagnostic(FrameworkDiagnostic diagnostic)=>Debug.LogWarning("["+diagnostic.Code+"] "+diagnostic.Message);
        public void Dispose(){context.Diagnostic-=OnDiagnostic;}
    }
}
