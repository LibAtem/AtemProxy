using System.Collections.Generic;
using System.Linq;

namespace AtemProxy
{
    public class CommandQueue
    {
        private readonly Dictionary<object, byte[]> _dict = new Dictionary<object, byte[]>();
        private readonly List<object> _keys = new List<object>();

        public void Clear()
        {
            lock (_dict)
            {
                _dict.Clear();
                _keys.Clear();
            }
        }

        public List<byte[]> Values()
        {
            lock (_dict)
            {
                return _keys.Select(k => _dict[k]).ToList();
            }
        }

        public void Set(object key, byte[] value)
        {
            lock (_dict)
            {
                if (_dict.TryGetValue(key, out var tmpval))
                {
                    _dict[key] = value;
                }
                else
                {
                    _dict.Add(key, value);
                    _keys.Add(key);
                }
            }
        }

    }
}