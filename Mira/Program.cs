namespace Mira
{
  using System;

  using Eto.Forms;
  using Eto.Mac.Forms.Controls;
  using MonoMac.AppKit;

  public static class Program
  {
    [STAThread]
    public static void Main(params string[] arguments)
    {
      try
      {
        string baseDirectory = "/Users/pic/Projects/Mira/Mira/System";
        string codeFilename = "Program.txt";
        if (arguments.Length == 2)
        {
          baseDirectory = arguments[0];
          codeFilename = arguments[1];
        }

        Eto.Style.Add<ListBoxHandler>("ListNative", handler => {
          handler.Control.FocusRingType = NSFocusRingType.None;
        });

        Application application = new Application();
        application.Run(new Editor(application, baseDirectory, codeFilename));
      }
      catch (Exception exception)
      {
        Console.Write(Editor.InnermostException(exception).Message);
      }
    }
  }
}