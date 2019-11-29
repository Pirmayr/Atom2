namespace Mira
{
  using System.Collections.Generic;

  public sealed class Items : List<object>
  {
    public Items()
    {
    }

    public Items(object instance)
    {
      Add(instance);
    }

    public Items(IEnumerable<object> collection) : base(collection)
    {
    }

    public static Items operator +(Items a, object b)
    {
      a.Add(b);
      return a;
    }

    public static Items operator +(object a, Items b)
    {
      b.Insert(0, a);
      return b;
    }

    public override string ToString()
    {
      return "(" + string.Join(" ", ToArray()) + ")";
    }
  }
}