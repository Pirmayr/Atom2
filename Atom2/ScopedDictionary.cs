using System.Collections.Generic;
using System.Linq;

#pragma warning disable 618

namespace Atom2
{
  public class ScopedDictionary<TK, TV>
  {
    public sealed class Scope : Dictionary<TK, TV> { }

    private sealed class Scopes : Stack<Scope> { }

    private readonly Scopes scopes = new Scopes();
    public Scope CurrentScope => scopes.Peek();

    protected ScopedDictionary()
    {
      EnterScope();
    }

    public void Add(TK key, TV value)
    {
      scopes.Peek().Add(key, value);
    }

    public bool ContainsKey(TK key)
    {
      foreach (Scope currentScope in scopes)
      {
        if (currentScope.ContainsKey(key))
        {
          return true;
        }
      }
      return false;
    }

    public void EnterScope()
    {
      scopes.Push(new Scope());
    }

    public void LeaveScope()
    {
      scopes.Pop();
    }

    // ReSharper disable once UnusedMember.Global
    public List<KeyValuePair<TK, TV>> ToList()
    {
      return scopes.Peek().ToList();
    }

    public bool TryGetValue(TK key, out TV value)
    {
      foreach (Scope currentScope in scopes)
      {
        if (currentScope.TryGetValue(key, out value))
        {
          return true;
        }
      }
      value = default;
      return false;
    }

    public TV this[TK key]
    {
      get => TryGetValue(key, out TV result) ? result : default;
      set => scopes.Peek()[key] = value;
    }
  }
}