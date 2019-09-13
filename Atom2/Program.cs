using System;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace Atom2
{
  internal static class MainClass
  {
    private static Expression<Func<T, T, T>> BuildIt<T>()
    {
      var paramExprA = Expression.Parameter(typeof(T), "a");
      var paramExprB = Expression.Parameter(typeof(T), "b");

      var body = Expression.Add(paramExprA, paramExprB);

      var lambda = Expression.Lambda<Func<T, T, T>>(body, paramExprA, paramExprB);

      return lambda;
    }

    public static void Main(string[] args)
    {
      try
      {
        var paramA = Expression.Parameter(typeof(int), "a");
        var paramB = Expression.Parameter(typeof(int), "b");
        var expression = Expression.Add(paramA, paramB);
        var lambda = Expression.Lambda(expression, paramA, paramB);
        var function = lambda.Compile();
        var result = function.GetType().InvokeMember("DynamicInvoke", BindingFlags.Public | BindingFlags.Instance | BindingFlags.InvokeMethod, null, function, new object[]{ 47, 11 });

        new System().Run(File.ReadAllText(args[0]));
        Console.Write("done");
      }
      catch (Exception exception)
      {
        Console.Write(exception.Message);
      }
    }
  }
}