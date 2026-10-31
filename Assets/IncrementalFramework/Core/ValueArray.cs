using System;
using System.Collections;
using System.Collections.Generic;

namespace IncrementalFramework.Core
{
    /// <summary>Indexed facade over entity Values. Owns no duplicate state.</summary>
    public sealed class ValueArray<T> : IReadOnlyList<T>
    {
        private readonly Func<int,T> read;
        private readonly Action<int,T> write;
        public int Length{get;}
        public int Count=>Length;
        public T this[int index]{get{Check(index);return read(index);}set{Check(index);write(index,value);}}
        public ValueArray(int length,Func<int,T>read,Action<int,T>write){Length=length;this.read=read;this.write=write;}
        private void Check(int index){if(index<0||index>=Length)throw new ArgumentOutOfRangeException(nameof(index));}
        public IEnumerator<T> GetEnumerator(){for(int i=0;i<Length;i++)yield return read(i);}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    }
}
