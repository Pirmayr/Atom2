using Eto.Mac;

namespace Mira
{
  using System;
  using System.Collections;
  using Eto.Forms;

  public static class Program
  {
    [STAThread]
    public static void Main(params string[] arguments)
    {
      try
      {
        SortedList list = new SortedList();

        list.Add("a", "1");
        list.Add("b", "2");

        foreach (DictionaryEntry currentItem in list)
        {
          Console.WriteLine(currentItem);
        }

        string baseDirectory = "/Users/pic/Projects/Mira/Mira/System";
        string codeFilename = "Program.txt";
        if (arguments.Length == 2)
        {
          baseDirectory = arguments[0];
          codeFilename = arguments[1];
        }
        Application application = new Application();
        application.Run(new Editor(application, baseDirectory, codeFilename));
      }
      catch (Exception exception)
      {
        Console.Write(exception.Message);
      }
    }
  }
}