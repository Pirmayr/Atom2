using System;
using System.IO;
using System.Windows.Forms;

namespace Atom2
{
  internal static class MainClass
  {
    public static void Main(string[] args)
    {
      try
      {
        new Runtime().Run(File.ReadAllText(args[0]));
        Console.Write("done");
      }
      catch (Exception exception)
      {
        Console.Write(exception.Message);
      }
    }
  }
}