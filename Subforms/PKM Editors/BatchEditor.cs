﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PKHeX
{
    public partial class BatchEditor : Form
    {
        public BatchEditor()
        {
            InitializeComponent();
            DragDrop += tabMain_DragDrop;
            DragEnter += tabMain_DragEnter;
            CB_Format.SelectedIndex = CB_Require.SelectedIndex = 0;
        }

        private const string CONST_RAND = "$rand";
        private int currentFormat = -1;
        private static readonly string[] pk6 = ReflectUtil.getPropertiesCanWritePublic(typeof(PK6)).OrderBy(i=>i).ToArray();
        private static readonly string[] pk5 = ReflectUtil.getPropertiesCanWritePublic(typeof(PK5)).OrderBy(i=>i).ToArray();
        private static readonly string[] pk4 = ReflectUtil.getPropertiesCanWritePublic(typeof(PK4)).OrderBy(i=>i).ToArray();
        private static readonly string[] pk3 = ReflectUtil.getPropertiesCanWritePublic(typeof(PK3)).OrderBy(i=>i).ToArray();
        private static readonly string[] all = pk6.Intersect(pk5).Intersect(pk4).Intersect(pk3).OrderBy(i => i).ToArray();
        private static readonly string[] any = pk6.Union(pk5).Union(pk4).Union(pk3).Distinct().OrderBy(i => i).ToArray();

        // GUI Methods
        private void B_Open_Click(object sender, EventArgs e)
        {
            if (!B_Go.Enabled) return;
            var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() != DialogResult.OK)
                return;

            TB_Folder.Text = fbd.SelectedPath;
            TB_Folder.Visible = true;
        }
        private void B_SAV_Click(object sender, EventArgs e)
        {
            TB_Folder.Text = "";
            TB_Folder.Visible = false;
        }
        private void B_Go_Click(object sender, EventArgs e)
        {
            if (b.IsBusy)
            { Util.Alert("Currently executing instruction list."); return; }

            if (RTB_Instructions.Lines.Any(line => line.Length == 0))
            { Util.Error("Line length error in instruction list."); return; }

            runBackgroundWorker();
        }

        private BackgroundWorker b = new BackgroundWorker { WorkerReportsProgress = true };
        private void runBackgroundWorker()
        {
            var Filters = getFilters().ToList();
            if (Filters.Any(z => string.IsNullOrWhiteSpace(z.PropertyValue)))
            { Util.Error("Empty Filter Value detected."); return; }

            var Instructions = getInstructions().ToList();
            if (Instructions.Any(z => string.IsNullOrWhiteSpace(z.PropertyValue)))
            { Util.Error("Empty Property Value detected."); return; }

            FLP_RB.Enabled = RTB_Instructions.Enabled = B_Go.Enabled = false;

            b = new BackgroundWorker {WorkerReportsProgress = true};

            b.DoWork += (sender, e) => {
                if (RB_SAV.Checked)
                {
                    var data = Main.SAV.BoxData;
                    setupProgressBar(data.Length);
                    processSAV(data, Filters, Instructions);
                }
                else
                {
                    var files = Directory.GetFiles(TB_Folder.Text, "*", SearchOption.AllDirectories);
                    setupProgressBar(files.Length);
                    processFolder(files, Filters, Instructions);
                }
            };
            b.ProgressChanged += (sender, e) =>
            {
                setProgressBar(e.ProgressPercentage);
            };
            b.RunWorkerCompleted += (sender, e) => {
                string result = $"Modified {ctr}/{len} files.";
                if (err > 0)
                    result += Environment.NewLine + $"{err} files ignored due to an internal error.";
                Util.Alert(result);
                FLP_RB.Enabled = RTB_Instructions.Enabled = B_Go.Enabled = true;
                setupProgressBar(0);
            };
            b.RunWorkerAsync();
        }

        // Progress Bar
        private void setupProgressBar(int count)
        {
            MethodInvoker mi = () => { PB_Show.Minimum = 0; PB_Show.Step = 1; PB_Show.Value = 0; PB_Show.Maximum = count; };
            if (PB_Show.InvokeRequired)
                PB_Show.Invoke(mi);
            else
                mi.Invoke();
        }
        private void setProgressBar(int i)
        {
            if (PB_Show.InvokeRequired)
                PB_Show.Invoke((MethodInvoker)(() => PB_Show.Value = i));
            else { PB_Show.Value = i; }
        }
        
        // Mass Editing
        private int ctr, len, err;
        private IEnumerable<StringInstruction> getFilters()
        {
            var raw =
                RTB_Instructions.Lines
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Where(line => new[] {'!','='}.Contains(line[0]));

            return from line in raw
                   let eval = line[0] == '='
                   let split = line.Substring(1).Split('=')
                   where split.Length == 2 && !string.IsNullOrWhiteSpace(split[0])
                   select new StringInstruction {PropertyName = split[0], PropertyValue = split[1], Evaluator = eval};
        }
        private IEnumerable<StringInstruction> getInstructions()
        {
            var raw =
                RTB_Instructions.Lines
                    .Where(line => !string.IsNullOrEmpty(line))
                    .Where(line => new[] {'.'}.Contains(line[0]))
                    .Select(line => line.Substring(1));

            return from line in raw
                   select line.Split('=') into split
                   where split.Length == 2
                   select new StringInstruction { PropertyName = split[0], PropertyValue = split[1] };
        }
        private void processSAV(PKM[] data, List<StringInstruction> Filters, List<StringInstruction> Instructions)
        {
            len = err = ctr = 0;
            for (int i = 0; i < data.Length; i++)
            {
                var pkm = data[i];
                ModifyResult r = ProcessPKM(pkm, Filters, Instructions);
                if (r != ModifyResult.Invalid)
                    len++;
                if (r == ModifyResult.Error)
                    err++;
                if (r == ModifyResult.Modified)
                    ctr++;

                b.ReportProgress(i);
            }

            Main.SAV.BoxData = data;
        }
        private void processFolder(string[] files, List<StringInstruction> Filters, List<StringInstruction> Instructions)
        {
            len = err = ctr = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                if (!PKX.getIsPKM(new FileInfo(file).Length))
                {
                    b.ReportProgress(i);
                    continue;
                }

                byte[] data = File.ReadAllBytes(file);
                var pkm = PKMConverter.getPKMfromBytes(data);
                ModifyResult r = ProcessPKM(pkm, Filters, Instructions);
                if (r != ModifyResult.Invalid)
                    len++;
                if (r == ModifyResult.Error)
                    err++;
                if (r == ModifyResult.Modified)
                {
                    ctr++;
                    File.WriteAllBytes(file, pkm.DecryptedBoxData);
                }

                b.ReportProgress(i);
            }
        }
        
        private void tabMain_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }
        private void tabMain_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (!Directory.Exists(files[0])) return;

            TB_Folder.Text = files[0];
            TB_Folder.Visible = true;
            RB_SAV.Checked = false;
            RB_Path.Checked = true;
        }

        // Utility Methods
        public class StringInstruction
        {
            public string PropertyName;
            public string PropertyValue;
            public bool Evaluator;
        }
        private enum ModifyResult
        {
            Invalid,
            Error,
            Filtered,
            Modified,
        }
        private static ModifyResult ProcessPKM(PKM PKM, IEnumerable<StringInstruction> Filters, IEnumerable<StringInstruction> Instructions)
        {
            if (!PKM.ChecksumValid || PKM.Species == 0)
                return ModifyResult.Invalid;

            Type pkm = PKM.GetType();

            foreach (var cmd in Filters)
            {
                try
                {
                    if (!pkm.HasProperty(cmd.PropertyName))
                        return ModifyResult.Filtered;
                    if (ReflectUtil.GetValueEquals(PKM, cmd.PropertyName, cmd.PropertyValue) != cmd.Evaluator)
                        return ModifyResult.Filtered;
                }
                catch
                {
                    Console.WriteLine($"Unable to compare {cmd.PropertyName} to {cmd.PropertyValue}.");
                    return ModifyResult.Filtered;
                }
            }

            ModifyResult result = ModifyResult.Error;
            foreach (var cmd in Instructions)
            {
                try
                {
                    if (cmd.PropertyValue == CONST_RAND && (cmd.PropertyName == "PID" || cmd.PropertyName == "EncryptionConstant"))
                        ReflectUtil.SetValue(PKM, cmd.PropertyName, Util.rnd32().ToString());
                    else
                        ReflectUtil.SetValue(PKM, cmd.PropertyName, cmd.PropertyValue);

                    result = ModifyResult.Modified;
                }
                catch { Console.WriteLine($"Unable to set {cmd.PropertyName} to {cmd.PropertyValue}."); }
            }
            return result;
        }

        private void B_Add_Click(object sender, EventArgs e)
        {
            if (CB_Property.SelectedIndex < 0)
            { Util.Alert("Invalid property selected."); return; }

            char[] prefix = {'.', '=', '!'};
            string s = prefix[CB_Require.SelectedIndex] + CB_Property.Items[CB_Property.SelectedIndex].ToString() + "=";
            if (RTB_Instructions.Lines.Length != 0 && RTB_Instructions.Lines.Last().Length > 0)
                s = Environment.NewLine + s;

            RTB_Instructions.AppendText(s);
        }

        private void CB_Format_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (currentFormat == CB_Format.SelectedIndex)
                return;

            CB_Property.Items.Clear();
            switch (CB_Format.SelectedIndex)
            {
                case 0: CB_Property.Items.AddRange(all.ToArray()); break; // All
                case 1: CB_Property.Items.AddRange(pk6.ToArray()); break;
                case 2: CB_Property.Items.AddRange(pk5.ToArray()); break;
                case 3: CB_Property.Items.AddRange(pk4.ToArray()); break;
                case 4: CB_Property.Items.AddRange(pk3.ToArray()); break;
                case 5: CB_Property.Items.AddRange(any.ToArray()); break; // Any
            }
            CB_Property.SelectedIndex = 0;
            currentFormat = CB_Format.SelectedIndex;
        }
    }
}