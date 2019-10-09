using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Atom2.System
{
  public class Editor : Form
  {
    private readonly RichTextBox editWindow = new RichTextBox();

    public static void Main(params string[] arguments)
    {
      try
      {


        /*
        new Runtime(arguments[0]).Run(arguments[1]);
        Console.Write("done");
        */

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(true);
        Application.Run(new Editor());
      }
      catch (Exception exception)
      {
        Console.Write(exception.Message);
      }
    }

    public Editor()
    {
      Font = new Font("Arial", 8.25f, FontStyle.Regular);

      editWindow.Dock = DockStyle.Fill;
      editWindow.Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Regular, GraphicsUnit.Point, ((byte) (0)));

      ListBox stackWindow = new ListBox();
      stackWindow.Dock = DockStyle.Left;
      stackWindow.IntegralHeight = false;

      foreach (FontFamily oneFontFamily in FontFamily.Families)
      {
        stackWindow.Items.Add(oneFontFamily.Name);
      }

      ListBox trackWindow = new ListBox();
      trackWindow.Dock = DockStyle.Right;
      trackWindow.IntegralHeight = false;

      RichTextBox outputWindow = new RichTextBox();
      outputWindow.Dock = DockStyle.Bottom;

      MenuStrip menu = new MenuStrip();
      menu.Items.Add("Open").Click += OnOpen;

      Controls.Add(editWindow);
      Controls.Add(stackWindow);
      Controls.Add(trackWindow);
      Controls.Add(outputWindow);
      Controls.Add(menu);

      WindowState = FormWindowState.Maximized;
    }

    private void OnOpen(object sender, EventArgs e)
    {
      using OpenFileDialog dialog = new OpenFileDialog();
      if (dialog.ShowDialog() == DialogResult.OK)
      {
        editWindow.Text = File.ReadAllText(dialog.FileName);
      }
    }
  }
}
