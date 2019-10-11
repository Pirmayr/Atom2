using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Items = System.Collections.Generic.List<object>;

namespace Atom2
{
  public sealed class WinformsEditor : Form
  {
    private readonly TextBox editWindow = new TextBox();
    private readonly Size minimalWindowSize = new Size(300, 300);
    private readonly TextBox outputWindow = new TextBox();
    private readonly Font standardFont = new Font("Verdana", 10);
    private readonly ListBox trackWindow = new ListBox();

    private WinformsEditor()
    {
      SuspendLayout();
      editWindow.Font = standardFont;
      editWindow.Multiline = true;
      editWindow.Dock = DockStyle.Fill;
      ListBox stackWindow = new ListBox {IntegralHeight = false, MinimumSize = minimalWindowSize, Sorted = true, Dock = DockStyle.Left};
      foreach (FontFamily oneFontFamily in FontFamily.Families)
      {
        stackWindow.Items.Add(oneFontFamily.Name);
      }
      trackWindow.Font = standardFont;
      trackWindow.IntegralHeight = false;
      trackWindow.MinimumSize = minimalWindowSize;
      trackWindow.Dock = DockStyle.Right;
      outputWindow.MinimumSize = minimalWindowSize;
      outputWindow.Dock = DockStyle.Bottom;
      MenuStrip menu = new MenuStrip();
      menu.Items.Add("Open").Click += OnOpen;
      menu.Items.Add("Run").Click += OnRun;
      Controls.Add(editWindow);
      Controls.Add(outputWindow);
      Controls.Add(stackWindow);
      Controls.Add(trackWindow);
      Controls.Add(menu);
      ResumeLayout();
      WindowState = FormWindowState.Maximized;
    }

    public static void Run(params string[] arguments)
    {
      Application.EnableVisualStyles();
      Application.SetCompatibleTextRenderingDefault(false);
      Application.Run(new WinformsEditor());
    }

    private static void RebuildTrackWindow(ListBox list, IEnumerable<object> rootItems, int indentation = 0)
    {
      string indentationPrefix = new string(' ', indentation * 2);
      foreach (object currentItem in rootItems)
      {
        if (currentItem is Items currentItems)
        {
          list.Items.Add(indentationPrefix + "(");
          RebuildTrackWindow(list, currentItems, indentation + 1);
          list.Items.Add(indentationPrefix + ")");
        }
        else
        {
          list.Items.Add(indentationPrefix + currentItem);
        }
      }
    }

    private void OnOpen(object sender, EventArgs e)
    {
      using (OpenFileDialog dialog = new OpenFileDialog())
      {
        if (dialog.ShowDialog() == DialogResult.OK)
        {
          editWindow.Text = File.ReadAllText(dialog.FileName);
        }
      }
    }

    private void OnRun(object sender, EventArgs e)
    {
      if (!Program.Runtime.Run(editWindow.Text, out Exception exception))
      {
        outputWindow.Text = exception.Message;
      }
      RebuildCodeWindow(trackWindow);
    }

    private void RebuildCodeWindow(ListBox list)
    {
      list.BeginUpdate();
      list.Items.Clear();
      RebuildTrackWindow(list, Program.Runtime.CurrentRootItems);
      list.EndUpdate();
    }
  }
}