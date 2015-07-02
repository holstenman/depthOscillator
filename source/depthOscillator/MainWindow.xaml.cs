/* ******************************************************************************************************************************
 *  
 * http://stackoverflow.com/questions/13380619/complete-guide-to-converting-code-from-kinect-sdk-beta-to-the-latest-kinect-sdk
 * 
 * sgalitsky 2015
 * reqs: Kinect SDK 1.8
 * ******************************************************************************************************************************/


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using Microsoft.Kinect;

using System.Windows.Forms; 

//osc
using System.Threading;
using System.IO;
using System.Net;
using Bespoke.Common.Osc;

// IronPython
using IronPython.Hosting;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using System.Dynamic;

using Midi;


using Axiom.Core;
using Axiom.Graphics;
using Axiom.Math;
using Axiom.Configuration;


namespace depthOscillator
{
    public partial class MainWindow : Window
    {
        private class outDeviceItem
        {
            public string Name;
            public int Value;
            public outDeviceItem(string name, int value)
            {
                Name = name; Value = value;
            }
            public override string ToString()
            {
                return Name;
            }
        }

        private KinectSensor sensor;
        private OutputDevice outputDevice;

        private static readonly TransportType TransportType = TransportType.Udp;
        private static readonly IPAddress MulticastAddress = IPAddress.Parse("127.0.0.1");//"224.25.26.27");
        private static readonly int Port = 7110;
        private static readonly IPEndPoint Destination = new IPEndPoint(MulticastAddress, Port);
        private static OscClient client = new OscClient();

        private readonly ScriptEngine m_engine;
        private readonly ScriptScope m_scope;

        private WriteableBitmap colorBitmap;
        private DepthImagePixel[] depthPixels;
        private byte[] colorPixels;

        


        LogWindow pyLogWin;

        Dictionary<JointType, Brush> jointColors = new Dictionary<JointType, Brush>() { 
            {JointType.HipCenter, new SolidColorBrush(Color.FromRgb(169, 176, 155))},
            {JointType.Spine, new SolidColorBrush(Color.FromRgb(169, 176, 155))},
            {JointType.ShoulderCenter, new SolidColorBrush(Color.FromRgb(168, 230, 29))},
            {JointType.Head, new SolidColorBrush(Color.FromRgb(200, 0,   0))},
            {JointType.ShoulderLeft, new SolidColorBrush(Color.FromRgb(79,  84,  33))},
            {JointType.ElbowLeft, new SolidColorBrush(Color.FromRgb(84,  33,  42))},
            {JointType.WristLeft, new SolidColorBrush(Color.FromRgb(255, 126, 0))},
            {JointType.HandLeft, new SolidColorBrush(Color.FromRgb(215,  86, 0))},
            {JointType.ShoulderRight, new SolidColorBrush(Color.FromRgb(33,  79,  84))},
            {JointType.ElbowRight, new SolidColorBrush(Color.FromRgb(33,  33,  84))},
            {JointType.WristRight, new SolidColorBrush(Color.FromRgb(77,  109, 243))},
            {JointType.HandRight, new SolidColorBrush(Color.FromRgb(37,   69, 243))},
            {JointType.HipLeft, new SolidColorBrush(Color.FromRgb(77,  109, 243))},
            {JointType.KneeLeft, new SolidColorBrush(Color.FromRgb(69,  33,  84))},
            {JointType.AnkleLeft, new SolidColorBrush(Color.FromRgb(229, 170, 122))},
            {JointType.FootLeft, new SolidColorBrush(Color.FromRgb(255, 126, 0))},
            {JointType.HipRight, new SolidColorBrush(Color.FromRgb(181, 165, 213))},
            {JointType.KneeRight, new SolidColorBrush(Color.FromRgb(71, 222,  76))},
            {JointType.AnkleRight, new SolidColorBrush(Color.FromRgb(245, 228, 156))},
            {JointType.FootRight, new SolidColorBrush(Color.FromRgb(77,  109, 243))}
        };

        public void log(string msg, string msgType)
        {
            pyLogWin.LogTextBox.AppendText(msgType + ": " + msg);
            switch (msgType)
            {
                case "error":
                    System.Windows.MessageBox.Show("Error: " + msg);
                    break;
                default:
                    break;
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            m_engine = Python.CreateEngine();
            dynamic scope = m_scope = m_engine.CreateScope();
            int i = 0;
            //scope.form = this;
            scope.k2o = CreateProxy();
            this.outDevices.Items.Clear();
            foreach (OutputDevice outDevice in OutputDevice.InstalledDevices)
            {
                this.outDevices.Items.Add(new outDeviceItem(outDevice.Name, i));
                i++;
            }
            reloadScript("process.py");
            initAxiom();
            pyLogWin = new LogWindow();
            pyLogWin.Show();
            

        }

        private void initAxiom()
        {
            /// http://axiom3d.net/wiki/index.php/ExampleApplication.cs
            /// 
            //IConfigurationManager ConfigurationManager = ConfigurationManagerFactory.CreateDefault();
            //using (var root = new Root("Game1.log"))
            //{
            //    if (ConfigurationManager.ShowConfigDialog(root))
            //    {
            //        RenderWindow window = root.Initialize(true);

            //        ResourceGroupManager.Instance.AddResourceLocation("media", "Folder", true);

            //        SceneManager scene = root.CreateSceneManager(SceneType.Generic);
            //        Camera camera = scene.CreateCamera("cam1");
            //        Viewport viewport = window.AddViewport(camera);

            //        TextureManager.Instance.DefaultMipmapCount = 5;
            //        ResourceGroupManager.Instance.InitializeAllResourceGroups();

            //        Entity penguin = scene.CreateEntity("bob", "penguin.mesh");
            //        SceneNode penguinNode = scene.RootSceneNode.CreateChildSceneNode();
            //        penguinNode.AttachObject(penguin);

            //        camera.Move(new Vector3(0, 0, 300));
            //        camera.LookAt(penguin.BoundingBox.Center);
            //        root.RenderOneFrame();
            //    }
            //    Console.Write("Press [Enter] to exit.");
            //    Console.ReadLine();
            //}
        }

        private void Window_Closed(object sender, System.ComponentModel.CancelEventArgs e)
        {
            pyLogWin.Close();
            if (null != this.sensor)
            {
                this.sensor.Stop();
            }
        }


        private void initKinect()
        {
            foreach (var potentialSensor in KinectSensor.KinectSensors)
            {
                if (potentialSensor.Status == KinectStatus.Connected)
                {
                    this.sensor = potentialSensor;
                    break;
                }
            }

            if (null != this.sensor)
            {

                var parameters = new TransformSmoothParameters
                {
                    Smoothing = 0.5f,//0.75f,
                    Correction = 0.9f,//0.5f
                    Prediction = 0.5f,
                    JitterRadius = 0.05f,//0.05f
                    MaxDeviationRadius = 0.02f //0.04f
                };
                this.sensor.SkeletonStream.Enable(parameters);
                this.sensor.SkeletonFrameReady += this.SensorSkeletonFrameReady;

                // Turn on the depth stream to receive depth frames
                this.sensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
                this.depthPixels = new DepthImagePixel[this.sensor.DepthStream.FramePixelDataLength];
                this.colorPixels = new byte[this.sensor.DepthStream.FramePixelDataLength * sizeof(int)];
                this.colorBitmap = new WriteableBitmap(this.sensor.DepthStream.FrameWidth, this.sensor.DepthStream.FrameHeight, 96.0, 96.0, PixelFormats.Bgr32, null);
                this.depth_image.Source = this.colorBitmap;
                this.sensor.DepthFrameReady += this.SensorDepthFrameReady;
            }

            try
            {
                this.sensor.Start();
            }
            catch (IOException)
            {
                this.sensor = null;
                return;
            }
        }

        private void Init_button_Click(object sender, RoutedEventArgs e)
        {
            initKinect();
        }
        
        
        private Point getDisplayPosition(Joint joint)
        {
            DepthImagePoint depthPoint = this.sensor.CoordinateMapper.MapSkeletonPointToDepthPoint(joint.Position, DepthImageFormat.Resolution640x480Fps30);
            return new Point(depthPoint.X, depthPoint.Y);
        }
        
        Polyline getBodySegment(Microsoft.Kinect.JointCollection joints, Brush brush, params JointType[] ids)
        {
            PointCollection points = new PointCollection(ids.Length);
            for (int i = 0; i < ids.Length; ++i)
            {
                points.Add(getDisplayPosition(joints[ids[i]]));
            }

            Polyline polyline = new Polyline();
            polyline.Points = points;
            polyline.Stroke = brush;
            polyline.StrokeThickness = 5;
            return polyline;
        }

        void SensorSkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            Skeleton[] skeletons = new Skeleton[0];
            int iSkeleton = 0;
            Brush[] brushes = new Brush[6];
            brushes[0] = new SolidColorBrush(Color.FromRgb(255, 0, 0));
            brushes[1] = new SolidColorBrush(Color.FromRgb(0, 255, 0));
            brushes[2] = new SolidColorBrush(Color.FromRgb(64, 255, 255));
            brushes[3] = new SolidColorBrush(Color.FromRgb(255, 255, 64));
            brushes[4] = new SolidColorBrush(Color.FromRgb(255, 64, 255));
            brushes[5] = new SolidColorBrush(Color.FromRgb(128, 128, 255));

            skeleton_image.Children.Clear();
            int i = 1;

            using (SkeletonFrame skeletonFrame = e.OpenSkeletonFrame())
            {
                if (skeletonFrame != null)
                {
                    skeletons = new Skeleton[skeletonFrame.SkeletonArrayLength];
                    skeletonFrame.CopySkeletonDataTo(skeletons);
                }
            }

            if (skeletons.Length != 0)
            {
                foreach (Skeleton data in skeletons)
                {
                    if (data.TrackingState== SkeletonTrackingState.Tracked)
                        {
                            sendSkeletonData(data, i);
                            // Draw bones
                            Brush brush = brushes[iSkeleton % brushes.Length];
                            skeleton_image.Children.Add(getBodySegment(data.Joints, brush, JointType.HipCenter, JointType.Spine, JointType.ShoulderCenter, JointType.Head));
                            skeleton_image.Children.Add(getBodySegment(data.Joints, brush, JointType.ShoulderCenter, JointType.ShoulderLeft, JointType.ElbowLeft, JointType.WristLeft, JointType.HandLeft));
                            skeleton_image.Children.Add(getBodySegment(data.Joints, brush, JointType.ShoulderCenter, JointType.ShoulderRight, JointType.ElbowRight, JointType.WristRight, JointType.HandRight));
                            skeleton_image.Children.Add(getBodySegment(data.Joints, brush, JointType.HipCenter, JointType.HipLeft, JointType.KneeLeft, JointType.AnkleLeft, JointType.FootLeft));
                            skeleton_image.Children.Add(getBodySegment(data.Joints, brush, JointType.HipCenter, JointType.HipRight, JointType.KneeRight, JointType.AnkleRight, JointType.FootRight));
                            // Draw joints
                            foreach (Joint joint in data.Joints)
                            {
                                Point jointPos = getDisplayPosition(joint);
                                Line jointLine = new Line();
                                jointLine.X1 = jointPos.X - 3;
                                jointLine.X2 = jointLine.X1 + 6;
                                jointLine.Y1 = jointLine.Y2 = jointPos.Y;
                                jointLine.Stroke = jointColors[joint.JointType];
                                jointLine.StrokeThickness = 6;
                                skeleton_image.Children.Add(jointLine);
                            }
                            i++;
                        }

                        iSkeleton++;
                    } // for each skeleton
                
            }
        }

        private void SensorDepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
        {
            using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
            {
                if (depthFrame != null)
                {
                    depthFrame.CopyDepthImagePixelDataTo(this.depthPixels);
                    int minDepth = depthFrame.MinDepth;
                    int maxDepth = depthFrame.MaxDepth;

                    int colorPixelIndex = 0;
                    for (int i = 0; i < this.depthPixels.Length; ++i)
                    {
                        short depth = depthPixels[i].Depth;
                        byte intensity = (byte)(depth >= minDepth && depth <= maxDepth ? depth : 0);
                        this.colorPixels[colorPixelIndex++] = intensity;
                        this.colorPixels[colorPixelIndex++] = intensity;
                        this.colorPixels[colorPixelIndex++] = intensity;
                        ++colorPixelIndex;
                    }
                    //if (m_scope.ContainsVariable("processDepthImage"))
                    //{
                    //    var processDepthImage = m_scope.GetVariable("processDepthImage");
                    //    log("result: " + processDepthImage(colorPixels).ToString(), "debug");
                    //}


                    this.colorBitmap.WritePixels(
                        new Int32Rect(0, 0, this.colorBitmap.PixelWidth, this.colorBitmap.PixelHeight),
                        this.colorPixels,
                        this.colorBitmap.PixelWidth * sizeof(int),
                        0);
                }
            }
        }

        private void Uninit_button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                sensor.Stop();
            }
            catch (InvalidOperationException)
            {
                System.Windows.MessageBox.Show("Runtime Uninitialization failed.");
                return;
            }
        }

        private void tilt_slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            tilt_label.Content = Math.Round(e.NewValue);
        }

        private void tilt_button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.sensor.ElevationAngle= Convert.ToInt32(tilt_label.Content);
            }
            catch (Exception exception)
            {
                System.Windows.MessageBox.Show("Could not change elevation angle to " + tilt_label.Content+". Exception:  " +exception.Message + "\n\n - Try to restart utility or/and reconnect Kinect sensor.");
            }
        }
        
        public void sendSkeletonData(Skeleton skeleton_data, int i )
        {
            IPEndPoint sourceEndPoint = new IPEndPoint(IPAddress.Loopback, Port);
            OscBundle sBundle = new OscBundle(sourceEndPoint);
            //OscMessage sMessage = new OscMessage(sourceEndPoint, "/skeleton", client);

            foreach (Joint joint in skeleton_data.Joints)
            {
                if (this.oscCheck.IsChecked.HasValue && this.oscCheck.IsChecked.Value)
                {
                    OscMessage oscMessage = new OscMessage(sourceEndPoint, "/skeleton", client);
                    oscMessage.Append(joint.JointType.ToString());
                    oscMessage.Append(i.ToString());
                    oscMessage.Append(joint.Position.X.ToString());
                    oscMessage.Append(joint.Position.Y.ToString());
                    oscMessage.Append(joint.Position.Z.ToString());
                    oscMessage.Send(Destination);
                }

            }
         }


        //private static void CheckReturnCode(Win32API.MMRESULT rc)
        //{
        //    if (rc != Win32API.MMRESULT.MMSYSERR_NOERROR)
        //    {
        //        StringBuilder errorMsg = new StringBuilder(128);
        //        rc = Win32API.midiOutGetErrorText(rc, errorMsg);
        //        if (rc != Win32API.MMRESULT.MMSYSERR_NOERROR)
        //        {
        //            throw new DeviceException("no error details");
        //        }
        //        throw new DeviceException(errorMsg.ToString());
        //    }
        //}


        //public void openMidiDevice()
        //{
        //    CheckReturnCode(Win32API.midiOutOpen(out handle, deviceId, null, (UIntPtr)0));
        //}


        // TODO: remove midi-dot-net from references - use Win32API instead
        //public void SendControlChange(int channel, int control, int value)
        //{
        //    CheckReturnCode(Win32API.midiOutShortMsg(handle, (UInt32)(0xB0 | (channel) | (control << 8) | (value << 16))));
        //    }
        //}

        // TODO:
        // if hand moves to one of 
        // process


        /// <summary>
        /// play MIDI note, function is callable from IronPython interpreter scope
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="note"></param>
        /// <param name="velo"></param>
        public void midiNote(int channel, int pitch, int velo)
        {
            try
            {
                // SendControlChange needs modification since only enumerated CC are available
                // do we need delay here? if for controlling Ableton Live only - no.
                // so no threads here)
                outputDevice.SendNoteOn((Channel)channel, (Pitch)pitch, velo);
                outputDevice.SendNoteOff((Channel)channel, (Pitch)pitch, velo);
            }
            catch (Midi.DeviceException exception)
            {
                log("MIDI exception: " + exception.Message, "error");
            }
        }

        /// <summary>
        /// send CC message
        /// function is callable from IronPython interpreter scope
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="note"></param>
        /// <param name="velo"></param>
        public void midiCC(int channel, int CC, int velo)
        {
            try
            {
                outputDevice.SendControlChange((Channel)channel, (Midi.Control)CC, velo);
            }
            catch (Midi.DeviceException exception)
            {
                log("MIDI exception: " + exception.Message, "error");
            }
        }

        public void reloadScript(string fName)
        {
            string scriptBody = File.ReadAllText(fName);
            m_engine.Execute(scriptBody, m_scope);
            dynamic scope = m_scope;
        }
        
        private void onReloadPy(object sender, RoutedEventArgs e)
        {
            reloadScript("process.py");
        }

        private void onLoadPy(object sender, RoutedEventArgs e)
        {
            OpenFileDialog theDialog = new OpenFileDialog();
            theDialog.Title = "Open python script file";
            theDialog.Filter = "python files|*.py";
            theDialog.InitialDirectory = @"C:\";
            string result = "process.py";
            if (theDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    result = theDialog.FileName;
                    reloadScript(result);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error: Could not read file from disk. Original error: " + ex.Message);
                }
            }
        }

        private object CreateProxy()
        {
            dynamic k2o = new ExpandoObject();
            k2o.midiNote = new Action<int, int, int>(midiNote);
            k2o.midiCC = new Action<int, int, int>(midiCC);
            k2o.log = new Action<string, string>(log);
            return k2o;
        }

        private void executeProcessMsg(object sender, RoutedEventArgs e)
        {
            if (m_scope.ContainsVariable("processDepthImage"))
            {
                byte[] ass = {1,2,1,2,3};
                var processDepthImage = m_scope.GetVariable("processDepthImage");
                log("result: " + processDepthImage(ass),"debug");
            }
            else
            {
                log("there is no processMsg at python script body.", "error");
            }
        }

        /// <summary>
        /// output MIDI device has been changed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void selectDevice(object sender, RoutedEventArgs e)
        {
            if (outputDevice != null) outputDevice.Close();
            try
            {
                outputDevice = OutputDevice.InstalledDevices[this.outDevices.SelectedIndex];
                outputDevice.Open();
            }
            catch (Midi.DeviceException exception)
            {
                log("MIDI device exception: " + exception.Message, "error");
            }
            

        }

    }
}
