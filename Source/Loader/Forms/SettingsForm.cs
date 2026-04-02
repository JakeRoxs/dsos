using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Loader
{
  public partial class SettingsForm : Form
  {
    public string ExeLocation = "";
    private bool DoNotSaveSettings = false;

    public SettingsForm()
    {
      InitializeComponent();
    }

    private void OnLoad(object sender, EventArgs e)
    {
      DoNotSaveSettings = true;
      UseSeperateSavesCheckbox.Checked = ProgramSettings.Default.use_seperate_saves;
      MasterServerUrlTextBox.Text = ProgramSettings.Default.hub_server_url;
      DoNotSaveSettings = false;

      UpdateState();
    }

    private void UpdateState()
    {
      CopySavesButton.Enabled = ProgramSettings.Default.use_seperate_saves;
    }

    private void CopySavesClicked(object sender, EventArgs e)
    {
      if (MessageBox.Show("This will overwrite any existing Rekindled Server saves that exist, are you sure you wish to do this?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
      {
        return;
      }

      int FilesCopied = 0;

      string BasePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\DarkSoulsIII";
      FilesCopied += CopySavesInDirectory(BasePath);

      BasePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\DarkSoulsII";
      FilesCopied += CopySavesInDirectory(BasePath);

      MessageBox.Show("Copied " + FilesCopied.ToString() + " retail saves to Rekindled Server.");
    }

    private int CopySavesInDirectory(string BasePath)
    {
      int FilesCopied = 0;

      if (!Directory.Exists(BasePath))
      {
        return 0;
      }

      string[] RetailFiles = System.IO.Directory.GetFiles(BasePath, "*.sl2", SearchOption.AllDirectories);
      foreach (string file in RetailFiles)
      {
        string NewPath = Path.ChangeExtension(file, ".rds");
        Console.WriteLine(file + " -> " + NewPath);

        File.Copy(file, NewPath, true);

        FilesCopied++;
      }

      return FilesCopied;
    }

    private void SettingChanged(object sender, EventArgs e)
    {
      if (DoNotSaveSettings)
      {
        return;
      }

      ProgramSettings.Default.use_seperate_saves = UseSeperateSavesCheckbox.Checked;
      ProgramSettings.Default.hub_server_url = MasterServerUrlTextBox.Text;
      ProgramSettings.Default.Save();

      UpdateState();
    }
  }
}
