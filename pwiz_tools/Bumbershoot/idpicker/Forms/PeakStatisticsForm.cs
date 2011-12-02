﻿//
// $Id$
//
// The contents of this file are subject to the Mozilla Public License
// Version 1.1 (the "License"); you may not use this file except in
// compliance with the License. You may obtain a copy of the License at
// http://www.mozilla.org/MPL/
//
// Software distributed under the License is distributed on an "AS IS"
// basis, WITHOUT WARRANTY OF ANY KIND, either express or implied. See the
// License for the specific language governing rights and limitations
// under the License.
//
// The Original Code is the IDPicker project.
//
// The Initial Developer of the Original Code is Matt Chambers.
//
// Copyright 2010 Vanderbilt University
//
// Contributor(s): Surendra Dasari
//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using NHibernate;
using NHibernate.Linq;
using DigitalRune.Windows.Docking;
using IDPicker.DataModel;
using ZedGraph;

using proteome = pwiz.CLI.proteome;
using msdata = pwiz.CLI.msdata;
using pwiz.CLI.chemistry;
using pwiz.CLI.analysis;

namespace IDPicker.Forms
{
    using DataFilter = DataModel.DataFilter;

    public partial class PeakStatisticsForm : DockableForm
    {
        public event EventHandler<SourceNotFoundEventArgs> SourceNotFound;

        //public DataGridView DataGridView { get { return dataGridView; } }

        #region Wrapper class for encapsulating query results
        public class SpectrumRow
        {
            public string SourceName { get; private set; }
            public Spectrum Spectrum { get; private set; }
            public PeptideSpectrumMatch PeptideSpectrumMatch { get; private set; }
            public string ModifiedSequence { get; private set; }

            #region Constructor
            public SpectrumRow (object[] queryRow)
            {
                SourceName = (string) queryRow[0];
                Spectrum = queryRow[1] as Spectrum;
                PeptideSpectrumMatch = queryRow[2] as PeptideSpectrumMatch;

                var mods = new Dictionary<int, List<double>>();
                if (!String.IsNullOrEmpty((string) queryRow[3]))
                {
                    var offsetMassDeltaPairs = ((string) queryRow[3]).Split(',');
                    foreach (var pair in offsetMassDeltaPairs)
                    {
                        var offsetAndMassDelta = pair.Split(':');
                        int offset = Convert.ToInt32(offsetAndMassDelta[0]);
                        if (!mods.ContainsKey(offset))
                            mods[offset] = new List<double>();
                        mods[offset].Add(Convert.ToDouble(offsetAndMassDelta[1]));
                    }
                }

                string format = String.Format("[{{0:f{0}}}]", 0);
                StringBuilder sb = new StringBuilder((string) queryRow[4]);
                foreach (var mod in (from m in mods orderby m.Key descending select m))
                    foreach (var massDelta in mod.Value)
                        if (mod.Key == int.MinValue)
                            sb.Insert(0, String.Format(format, massDelta));
                        else if (mod.Key == int.MaxValue || mod.Key >= sb.Length)
                            sb.AppendFormat(format, massDelta);
                        else
                            sb.Insert(mod.Key + 1, String.Format(format, massDelta));
                ModifiedSequence = sb.ToString();
            }
            #endregion
        }
        #endregion

        private enum IonSeries
        {
            a, b, c, cMinus1,
            x, y, z, zPlus1, zPlus2,
            Count
        }

        private string[] IonSeriesLabels =
        {
            "a", "b", "c", "c-1",
            "x", "y", "z", "z+1", "z+2"
        };

        LineItem precursorScatterPlot, chargeReducedScatterPlot;

        public PeakStatisticsForm (IDPickerForm owner)
        {
            InitializeComponent();

            this.owner = owner;

            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                e.Cancel = true;
                DockState = DockState.DockBottomAutoHide;
            };

            Text = TabText = "Peak Statistics";

            var csr = new ColorSymbolRotator();
            var colors = new List<Color>() { Color.Gray };
            for (int i = 0; i < (int) IonSeries.Count; ++i)
                colors.Add(csr.NextColor);

            var graphPane = zedGraphControl.GraphPane;
            graphPane.Title.Text = "Peak m/z - precursor m/z";
            graphPane.XAxis.Title.Text = "m/z";
            graphPane.YAxis.Title.Text = "Intensity";
            graphPane.YAxis.MajorTic.IsOpposite = false;
            graphPane.YAxis.MinorTic.IsOpposite = false;
            graphPane.Legend.IsVisible = false;
            graphPane.IsFontsScaled = false;

            precursorScatterPlot = graphPane.AddCurve("", new PointPairList(), Color.Gray, SymbolType.Circle);
            precursorScatterPlot.Line.IsVisible = false;
            precursorScatterPlot.Symbol.IsAntiAlias = false;
            precursorScatterPlot.Symbol.Border.IsVisible = false;
            precursorScatterPlot.Symbol.Fill = new Fill(colors.ToArray()) { Type = FillType.GradientByColorValue, RangeMin = 0, RangeMax = (double) IonSeries.Count, SecondaryValueGradientColor = Color.Gray };
            precursorScatterPlot.Symbol.Size = 5f;

            graphPane = new GraphPane();
            graphPane.Title.Text = "Peak m/z - charge reduced precursor m/z";
            graphPane.XAxis.Title.Text = "m/z";
            graphPane.YAxis.Title.Text = "Intensity";
            graphPane.YAxis.MajorTic.IsOpposite = false;
            graphPane.YAxis.MinorTic.IsOpposite = false;
            graphPane.Legend.IsVisible = false;
            graphPane.IsFontsScaled = false;
            zedGraphControl.MasterPane.PaneList.Add(graphPane);

            chargeReducedScatterPlot = graphPane.AddCurve("", new PointPairList(), Color.Gray, SymbolType.Circle);
            chargeReducedScatterPlot.Line.IsVisible = false;
            chargeReducedScatterPlot.Symbol.IsAntiAlias = false;
            chargeReducedScatterPlot.Symbol.Border.IsVisible = false;
            chargeReducedScatterPlot.Symbol.Fill = new Fill(colors.ToArray()) { Type = FillType.GradientByColorValue, RangeMin = 0, RangeMax = (double) IonSeries.Count, SecondaryValueGradientColor = Color.Gray };
            chargeReducedScatterPlot.Symbol.Size = 5f;

            zedGraphControl.MasterPane.SetLayout(zedGraphControl.CreateGraphics(), PaneLayout.SingleColumn);
            zedGraphControl.MasterPane.InnerPaneGap = 0;

            zedGraphControl.MasterPane.AxisChange();
            zedGraphControl.Refresh();
        }

        private IDPickerForm owner;
        private NHibernate.ISession session;

        private DataFilter viewFilter; // what the user has filtered on
        private DataFilter dataFilter; // how this view is filtered (i.e. never on its own rows)
        private DataFilter basicDataFilter; // the basic filter without the user filtering on rows

        private List<double> fragmentationStatistics, basicPeakStatistics;

        private List<double> getPeakStatistics ()
        {
            IList<object[]> queryRows;
            lock (session)
                queryRows = session.CreateQuery("SELECT psm.Spectrum.Source.Name, psm.Spectrum, psm, DISTINCT_GROUP_CONCAT(pm.Offset || ':' || mod.MonoMassDelta), psm.Peptide.Sequence " +
                                                viewFilter.GetFilteredQueryString(DataFilter.FromPeptideSpectrumMatch, DataFilter.PeptideSpectrumMatchToModification) +
                                                " GROUP BY psm.Spectrum.id ")
                                   .List<object[]>();
            var spectrumRows = queryRows.Select(o => new SpectrumRow(o)).Shuffle().Take(1000).OrderBy(o => o.SourceName);

            precursorScatterPlot.Clear();
            chargeReducedScatterPlot.Clear();

            int spectraCount = 0;

            string spectrumListFilters = String.Empty;
            Invoke(new MethodInvoker(() =>
            {
                spectrumListFilters = spectrumFiltersTextBox.Text;
                zedGraphControl.MasterPane.AxisChange();
                zedGraphControl.Refresh();
            }));

            var points = new PointPairList();

            string currentSourceName = null;
            string currentSourcePath = null;
            msdata.MSData msd = null;

            lock(owner)
            foreach (var row in spectrumRows)
            {
                if (row.SourceName != currentSourceName)
                {
                    currentSourceName = row.SourceName;
                    currentSourcePath = locateSpectrumSource(currentSourceName);
                    msd = new pwiz.CLI.msdata.MSDataFile(currentSourcePath);

                    //var param = session.Query<AnalysisParameter>().Where(o => o.Name == "SpectrumListFilters").Min(o => o.Value);
                    //string spectrumListFilters = String.IsNullOrEmpty(param) ? String.Empty : param;
                    SpectrumListFactory.wrap(msd, spectrumListFilters.Split(";".ToCharArray(), StringSplitOptions.RemoveEmptyEntries));
                }

                string label = String.Format("{0}/{1}\n{2}", row.SourceName, msdata.id.abbreviate(row.Spectrum.NativeID), row.ModifiedSequence);

                var spectrumList = msd.run.spectrumList;

                ++spectraCount;

                var pwizPeptide = new proteome.Peptide(row.ModifiedSequence, proteome.ModificationParsing.ModificationParsing_Auto,
                                                       proteome.ModificationDelimiter.ModificationDelimiter_Brackets);
                var fragmentation = pwizPeptide.fragmentation(true, true);

                var pwizSpectrum = spectrumList.spectrum(spectrumList.find(row.Spectrum.NativeID), true);
                var pointMap = new seems.PointMap(new ZedGraph.PointPairList(pwizSpectrum.getMZArray().data, pwizSpectrum.getIntensityArray().data));
                double tic = pointMap.Values.Sum();

                double precursorMz = row.Spectrum.PrecursorMZ;
                double chargeReducedPrecursorMz = precursorMz * row.PeptideSpectrumMatch.Charge;

                bool plotMatchedPeaks = true;
                bool removeMatchedPeaks = false;

                double tolerance = 0.03;

                for (int z = 1; z <= 1; ++z)
                for (int length = 1, end = pwizPeptide.sequence.Length; length <= end; ++length)
                {
                    seems.PointMap.Enumerator itr;

                    int series = 0;

                    string NTermFragment = row.ModifiedSequence.Substring(0, length);
                    string CTermFragment = row.ModifiedSequence.Substring(row.ModifiedSequence.Length - length);

                    // a
                    itr = pointMap.FindNear(fragmentation.a(length, z), tolerance);
                    if (itr != null && itr.IsValid)
                    {
                        if (plotMatchedPeaks)
                        {
                            precursorScatterPlot.AddPoint(new PointPair(itr.Current.Key - precursorMz, itr.Current.Value / tic, ++series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, precursorMz, NTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                            chargeReducedScatterPlot.AddPoint(new PointPair(itr.Current.Key - chargeReducedPrecursorMz, itr.Current.Value / tic, series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, chargeReducedPrecursorMz, NTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                        }

                        if (removeMatchedPeaks)
                            pointMap.Remove(itr);
                    }
                    else
                        ++series;

                    // b
                    itr = pointMap.FindNear(fragmentation.b(length, z), tolerance);
                    if (itr != null && itr.IsValid)
                    {
                        if (plotMatchedPeaks)
                        {
                            precursorScatterPlot.AddPoint(new PointPair(itr.Current.Key - precursorMz, itr.Current.Value / tic, ++series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, precursorMz, NTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                            chargeReducedScatterPlot.AddPoint(new PointPair(itr.Current.Key - chargeReducedPrecursorMz, itr.Current.Value / tic, series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, chargeReducedPrecursorMz, NTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                        }

                        if (removeMatchedPeaks)
                            pointMap.Remove(itr);
                    }
                    else
                        ++series;

                    if (length != pwizPeptide.sequence.Length)
                    {
                        // c
                        itr = pointMap.FindNear(fragmentation.c(length, z), tolerance);
                        if (itr != null && itr.IsValid)
                        {
                            if (plotMatchedPeaks)
                            {
                                precursorScatterPlot.AddPoint(new PointPair(itr.Current.Key - precursorMz, itr.Current.Value / tic, ++series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, precursorMz, NTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                                chargeReducedScatterPlot.AddPoint(new PointPair(itr.Current.Key - chargeReducedPrecursorMz, itr.Current.Value / tic, series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, chargeReducedPrecursorMz, NTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                            }

                            if (removeMatchedPeaks)
                                pointMap.Remove(itr);
                        }
                        else
                            ++series;

                        // c-1
                        itr = pointMap.FindNear(fragmentation.c(length, z) - Proton.Mass / z, tolerance);
                        if (itr != null && itr.IsValid)
                        {
                            if (plotMatchedPeaks)
                            {
                                precursorScatterPlot.AddPoint(new PointPair(itr.Current.Key - precursorMz, itr.Current.Value / tic, ++series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, precursorMz, NTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                                chargeReducedScatterPlot.AddPoint(new PointPair(itr.Current.Key - chargeReducedPrecursorMz, itr.Current.Value / tic, series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, chargeReducedPrecursorMz, NTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                            }

                            if (removeMatchedPeaks)
                                pointMap.Remove(itr);
                        }
                        else
                            ++series;

                        // x
                        itr = pointMap.FindNear(fragmentation.x(length, z), tolerance);
                        if (itr != null && itr.IsValid)
                        {
                            if (plotMatchedPeaks)
                            {
                                precursorScatterPlot.AddPoint(new PointPair(itr.Current.Key - precursorMz, itr.Current.Value / tic, ++series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, precursorMz, CTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                                chargeReducedScatterPlot.AddPoint(new PointPair(itr.Current.Key - chargeReducedPrecursorMz, itr.Current.Value / tic, series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, chargeReducedPrecursorMz, CTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                            }

                            if (removeMatchedPeaks)
                                pointMap.Remove(itr);
                        }
                        else
                            ++series;
                    }
                    else
                        series += 3;

                    // y
                    itr = pointMap.FindNear(fragmentation.y(length, z), tolerance);
                    if (itr != null && itr.IsValid)
                    {
                        if (plotMatchedPeaks)
                        {
                            precursorScatterPlot.AddPoint(new PointPair(itr.Current.Key - precursorMz, itr.Current.Value / tic, ++series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, precursorMz, CTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                            chargeReducedScatterPlot.AddPoint(new PointPair(itr.Current.Key - chargeReducedPrecursorMz, itr.Current.Value / tic, series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, chargeReducedPrecursorMz, CTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                        }

                        if (removeMatchedPeaks)
                            pointMap.Remove(itr);
                    }
                    else
                        ++series;

                    // z
                    itr = pointMap.FindNear(fragmentation.z(length, z), tolerance);
                    if (itr != null && itr.IsValid)
                    {
                        if (plotMatchedPeaks)
                        {
                            precursorScatterPlot.AddPoint(new PointPair(itr.Current.Key - precursorMz, itr.Current.Value / tic, ++series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, precursorMz, CTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                            chargeReducedScatterPlot.AddPoint(new PointPair(itr.Current.Key - chargeReducedPrecursorMz, itr.Current.Value / tic, series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, chargeReducedPrecursorMz, CTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                        }

                        if (removeMatchedPeaks)
                            pointMap.Remove(itr);
                    }
                    else
                        ++series;

                    // z+1
                    itr = pointMap.FindNear(fragmentation.zRadical(length, z), tolerance);
                    if (itr != null && itr.IsValid)
                    {
                        if (plotMatchedPeaks)
                        {
                            precursorScatterPlot.AddPoint(new PointPair(itr.Current.Key - precursorMz, itr.Current.Value / tic, ++series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, precursorMz, CTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                            chargeReducedScatterPlot.AddPoint(new PointPair(itr.Current.Key - chargeReducedPrecursorMz, itr.Current.Value / tic, series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, chargeReducedPrecursorMz, CTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                        }

                        if (removeMatchedPeaks)
                            pointMap.Remove(itr);
                    }

                    // z+2
                    itr = pointMap.FindNear(fragmentation.zRadical(length, z) + Proton.Mass / z, tolerance);
                    if (itr != null && itr.IsValid)
                    {
                        if (plotMatchedPeaks)
                        {
                            precursorScatterPlot.AddPoint(new PointPair(itr.Current.Key - precursorMz, itr.Current.Value / tic, ++series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, precursorMz, CTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                            chargeReducedScatterPlot.AddPoint(new PointPair(itr.Current.Key - chargeReducedPrecursorMz, itr.Current.Value / tic, series, String.Format("{0} {1}\n{2} {3} {4} {5}", label, chargeReducedPrecursorMz, CTermFragment, itr.Current.Key, IonSeriesLabels[series - 1], length)));
                        }

                        if (removeMatchedPeaks)
                            pointMap.Remove(itr);
                    }
                    else
                        ++series;
                }

                foreach (var pair in pointMap)
                {
                    precursorScatterPlot.AddPoint(new PointPair(pair.Key - precursorMz, pair.Value/tic, 0, label));
                    chargeReducedScatterPlot.AddPoint(new PointPair(pair.Key - chargeReducedPrecursorMz, pair.Value/tic, 0, label));
                }

                if ((spectraCount % 100) == 0)
                {
                    Invoke(new MethodInvoker(() =>
                    {
                        zedGraphControl.MasterPane.AxisChange();
                        zedGraphControl.Refresh();
                    }));
                }
            }

            Invoke(new MethodInvoker(() =>
            {
                if (!lockZoomCheckBox.Checked)
                {
                    zedGraphControl.ZoomOutAll(zedGraphControl.GraphPane);
                }
                zedGraphControl.MasterPane.AxisChange();
                zedGraphControl.Refresh();
            }));
            return new List<double>(); //percentTicBySpectrumByFragmentType[1];
        }

        public void SetData (NHibernate.ISession session, DataFilter dataFilter)
        {
            this.session = session;
            viewFilter = dataFilter;
            this.dataFilter = new DataFilter(dataFilter);

            ClearData();
        }

        public void ClearData ()
        {
            Text = TabText = "Peak Statistics";

            //dataGridView.Rows.Clear();
            //dataGridView.Refresh();
            Refresh();
        }

        public void ClearData (bool clearBasicFilter)
        {
            if (clearBasicFilter)
                basicDataFilter = null;
            ClearData();
        }

        void setData (object sender, DoWorkEventArgs e)
        {
            try
            {
                if (dataFilter.IsBasicFilter)
                {
                    // refresh basic data when basicDataFilter is unset or when the basic filter values have changed
                    if (basicDataFilter == null || (dataFilter.IsBasicFilter && dataFilter != basicDataFilter))
                    {
                        basicDataFilter = new DataFilter(dataFilter);
                        basicPeakStatistics = getPeakStatistics();
                    }

                    fragmentationStatistics = basicPeakStatistics;
                }
                else
                    fragmentationStatistics = getPeakStatistics();
            }
            catch (Exception ex)
            {
                Invoke(new MethodInvoker(() => Program.HandleException(ex)));
            }
        }

        void renderData (object sender, RunWorkerCompletedEventArgs e)
        {
            Text = TabText = "Peak Statistics";           

            //dataGridView.Rows.Clear();
            //dataGridView.Rows.Add(fragmentationStatistics.Cast<object>().ToArray());
            //dataGridView.Refresh();
        }

        string locateSpectrumSource (string spectrumSourceName)
        {
            try
            {
                return Util.FindSourceInSearchPath(spectrumSourceName, ".");
            }
            catch
            {
                try
                {
                    return Util.FindSourceInSearchPath(spectrumSourceName, Properties.Settings.Default.LastSpectrumSourceDirectory);
                }
                catch
                {
                    if (SourceNotFound != null)
                    {
                        var eventArgs = new SourceNotFoundEventArgs() {SourcePath = spectrumSourceName};
                        SourceNotFound(this, eventArgs);
                        if (System.IO.File.Exists(eventArgs.SourcePath) || System.IO.Directory.Exists(eventArgs.SourcePath))
                        {
                            Properties.Settings.Default.LastSpectrumSourceDirectory = System.IO.Path.GetDirectoryName(eventArgs.SourcePath);
                            Properties.Settings.Default.Save();
                            return eventArgs.SourcePath;
                        }
                    }

                    throw;
                }
            }
        }

        #region Export stuff
        private void clipboardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //var table = getFormTable();

            //TableExporter.CopyToClipboard(table);
        }

        private void fileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //var table = getFormTable();

            //TableExporter.ExportToFile(table);
        }

        private void exportButton_Click(object sender, EventArgs e)
        {
            /*if (dataGridView.SelectedCells.Count > 1)
            {
                exportMenu.Items[0].Text = "Copy Selected to Clipboard";
                exportMenu.Items[1].Text = "Export Selected to File";
                exportMenu.Items[2].Text = "Show Selected in Excel";
            }
            else
            {
                exportMenu.Items[0].Text = "Copy to Clipboard";
                exportMenu.Items[1].Text = "Export to File";
                exportMenu.Items[2].Text = "Show in Excel";
            }*/

            exportMenu.Show(Cursor.Position);
        }

        /*private List<List<string>> getFormTable()
        {
            var table = new List<List<string>>();
            var row = new List<string>();

            if (dataGridView.SelectedCells.Count > 1)
            {
                var rowList = new List<int>();
                var columnList = new List<int>();

                foreach (DataGridViewCell cell in dataGridView.SelectedCells)
                {
                    if (!rowList.Contains(cell.RowIndex))
                        rowList.Add(cell.RowIndex);
                    if (!columnList.Contains(cell.ColumnIndex))
                        columnList.Add(cell.ColumnIndex);
                }
                rowList.Sort();
                columnList.Sort();

                //get column names
                for (int x = 0; x < columnList.Count; x++)
                    row.Add(dataGridView.Columns[columnList[x]].HeaderText);

                table.Add(row);
                row = new List<string>();

                //Retrieve all items
                for (int tableRow = 0; tableRow < rowList.Count; tableRow++)
                {
                    //row.Add(dataGridView.Rows[tableRow].HeaderCell.Value.ToString());
                    for (int x = 0; x < columnList.Count; x++)
                        row.Add(dataGridView[columnList[x], rowList[tableRow]].Value.ToString());

                    table.Add(row);
                    row = new List<string>();
                }
            }
            else
            {
                //get column names
                for (int x = 0; x < dataGridView.Columns.Count; x++)
                    row.Add(dataGridView.Columns[x].HeaderText);

                table.Add(row);
                row = new List<string>();

                //Retrieve all items
                for (int tableRow = 0; tableRow < dataGridView.Rows.Count; tableRow++)
                {
                    //row.Add(dataGridView.Rows[tableRow].HeaderCell.Value.ToString());
                    for (int x = 0; x < dataGridView.Columns.Count; x++)
                        row.Add(dataGridView[x, tableRow].Value.ToString());

                    table.Add(row);
                    row = new List<string>();
                }
            }

            return table;
        }*/

        private void showInExcelToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //var table = getFormTable();

            //TableExporter.ShowInExcel(table);
        }
        #endregion

        bool spectrumFiltersDirty = false;
        private void spectrumFiltersTextBox_TextChanged (object sender, EventArgs e)
        {
            spectrumFiltersDirty = true;
        }

        private void spectrumFiltersTextBox_Leave (object sender, EventArgs e)
        {
            if (spectrumFiltersDirty)
            {
                spectrumFiltersDirty = false;
                basicDataFilter = null; // force refresh
                SetData(session, viewFilter);
            }
        }

        private void refreshButton_Click (object sender, EventArgs e)
        {
            Text = TabText = "Loading peak statistics...";

            var workerThread = new BackgroundWorker()
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };

            workerThread.DoWork += new DoWorkEventHandler(setData);
            workerThread.RunWorkerCompleted += new RunWorkerCompletedEventHandler(renderData);
            workerThread.RunWorkerAsync();
        }
    }
}