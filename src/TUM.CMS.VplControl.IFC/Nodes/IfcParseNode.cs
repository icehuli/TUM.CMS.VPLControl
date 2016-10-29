﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TUM.CMS.VplControl.Core;
using System.Linq;
using System.Windows.Media;
using TUM.CMS.VplControl.IFC.Utilities;
using Xbim.Common;
using Xbim.Common.Metadata;
using Xbim.Common.Step21;
using Xbim.Ifc;
using Xbim.Presentation;

namespace TUM.CMS.VplControl.IFC.Nodes
{
    public class IfcParseNode : Node
    {
        public IfcStore xModel;
        public IfcParseNode(Core.VplControl hostCanvas) : base(hostCanvas)
        {
            AddInputPortToNode("Test", typeof(string));

            AddOutputPortToNode("GUID", typeof(object));

            var label = new Label
            {
                Content = "IFC File Reading",
                Width = 130,
                FontSize = 15,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            var textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10,
                Padding = new Thickness(5),
                IsHitTestVisible = false
            };

            var button = new Button { Content = "Clean Database" };
            button.Click += button_Click;

            AddControlToNode(label);
            AddControlToNode(textBlock);
            AddControlToNode(button);

        }

        /// <summary>
        /// Cleaning the DataController
        /// 
        /// When Reading Files, they are stored in the DataController.
        /// To prevent a big DataController, the user can delete all stored Models except the actual Model
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void button_Click(object sender, RoutedEventArgs e)
        {
            var models = DataController.Instance.ModelStorage.ToList();
            if (models.Count > 1)
            {
                for (int i = 0; i < models.Count - 1; i++)
                {
                    DataController.Instance.RemoveModel(models[i].Key);
                }
            }

        }
        
        private BackgroundWorker _worker;
        /// <summary>
        /// Reads the file String and looks if its existing.
        /// Create a new xModel inside the Temp Folder with a Random Number in the FileName
        /// Safe the Model in an Dictonary (DataController)
        /// 
        /// Output is the GUID (FilePath)
        /// </summary>
        public override void Calculate()
        {
            var file = InputPorts[0].Data.ToString();
            if (file != "" && File.Exists(file))
            {
                _worker = new BackgroundWorker();
                _worker.DoWork += new DoWorkEventHandler(worker_DoWork);
                _worker.RunWorkerAsync(file);
                _worker.RunWorkerCompleted += new RunWorkerCompletedEventHandler(worker_RunWorkerCompleted);
            }
            else
            {
                var textBlock = ControlElements[1] as TextBlock;
                textBlock.Background = Brushes.Red;
                textBlock.Text = "Please select a true File!";
            }



        }
        
        /// <summary>
        /// Background Worker
        /// 
        /// Create xBIM File and Add it to the new DataController.
        /// The xBIM File is therefor stored in a Database
        /// 
        /// The ModelId (FilePath) and the ElementList are stored in the ModelInfo Class
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e">Value File is the FilePath</param>
        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            var file = e.Argument.ToString();
            Random zufall = new Random();
            int number = zufall.Next(1, 1000);

            string result = Path.GetTempPath();
            string copyFile = result + "copy" + number + ".ifc";
            while (File.Exists(copyFile))
            {
                number = zufall.Next(1, 1000);
                copyFile = result + "copy" + number + ".ifc";
            }

            

            using (xModel = IfcStore.Open(file))
            {

                var newModel = IfcStore.Create(xModel.IfcSchemaVersion, XbimStoreType.InMemoryModel);
                //var newModel = IfcStore.Create(IfcSchemaVersion.Ifc2X3, XbimStoreType.InMemoryModel);
                PropertyTranformDelegate propTransform = delegate(ExpressMetaProperty prop, object toCopy)
                {
                    var value = prop.PropertyInfo.GetValue(toCopy, null);
                    return value;
                };

                
                using (var txn = newModel.BeginTransaction())
                {
                    var copied = new XbimInstanceHandleMap(xModel, newModel);
                    if (xModel.IfcSchemaVersion == IfcSchemaVersion.Ifc2X3)
                    {
                        foreach (var item in xModel.Instances.OfType<Xbim.Ifc2x3.Kernel.IfcProduct>())
                        {
                            newModel.InsertCopy(item, copied, propTransform, true, true);
                        }
                    }
                    else
                    {
                        foreach (var item in xModel.Instances.OfType<Xbim.Ifc4.Kernel.IfcProduct>())
                        {
                            newModel.InsertCopy(item, copied, propTransform, true, true);
                        }
                    }

                    
                    txn.Commit();
                    newModel.SaveAs(copyFile);
                }
                newModel.Close();
            }


            xModel.Close();

            DataController.Instance.AddModel(copyFile, xModel);



            xModel = DataController.Instance.GetModel(copyFile);
            if (xModel.IfcSchemaVersion == IfcSchemaVersion.Ifc2X3)
            {
                ModelInfoIFC2x3 modelInfo = new ModelInfoIFC2x3(copyFile);
                List< Xbim.Ifc2x3.Kernel.IfcProduct> elements = xModel.Instances.OfType< Xbim.Ifc2x3.Kernel.IfcProduct> ().ToList();
                foreach (var element in elements)
                {
                    modelInfo.AddElementIds(element.GlobalId);
                }
                e.Result = modelInfo;
            }
            else
            {
                ModelInfoIFC4 modelInfo = new ModelInfoIFC4(copyFile);
                List< Xbim.Ifc4.Kernel.IfcProduct> elements = xModel.Instances.OfType< Xbim.Ifc4.Kernel.IfcProduct> ().ToList();
                foreach (var element in elements)
                {
                    modelInfo.AddElementIds(element.GlobalId);
                }
                e.Result = modelInfo;
            }

            
            

           
        }

        /// <summary>
        /// Its important to split the Creation of the xBIM File and the OutputPort
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e">Result is the modelInfo Class</param>
        private void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            try
            {
                OutputPorts[0].Data = e.Result;
                var textBlock = ControlElements[1] as TextBlock;
                textBlock.Background = Brushes.White;
                textBlock.Text = "File is Valid!";
            }
            catch (Exception exception)
            {
                Console.WriteLine("An error occurred: '{0}'", exception);
            }
            DataController.Instance.CloseModel(xModel);
            
        }



            
       

        public override Node Clone()
        {
            return new IfcParseNode(HostCanvas)
            {
                Top = Top,
                Left = Left
            };
        }



    }
}