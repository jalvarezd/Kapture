using System;
using System.Collections.Generic;
using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
//using System.Windows.Data;
//using System.Windows.Documents;
//using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
//using System.Windows.Navigation;
using System.Windows.Shapes;

using Microsoft.Kinect; // Kinect Resources
using System.IO;        // File management
using System.ComponentModel;
//using System.Drawing;



namespace Kapture
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    
    /// this enum will be used as a switch between visualization modes 
    public enum DisplayFrameType
    {
        InfraredOnly,
        DepthOnly, 
        BodyJointsOnly,
        BodyJointsAdd
    }
    public enum RecordingStatus
    {
        Recording,
        Pause,
        Cancel,
        Save,
        VisualizeOnly
    }
    public enum RecordingsFileExtension
    {
        CSV,
        PNG,
        BMP
    }
    public enum RecordingsFrameToSave
    {
        Infrared,
        Depth,
        BodyJoints
    }
    public partial class MainWindow : Window {


        // KINECT VARIABLES:
        // Kinect definition:
        private KinectSensor kinect = null;
        // Readers for IR, Depth and Skeleton Frames
        private MultiSourceFrameReader multiSourceFrameReader = null;
        // Frame Descriptions:
        private FrameDescription infrFrameDescription = null;
        private FrameDescription depthFrameDescription = null;

        // VISUALIZATION VARIABLES DEFINITION:
        // default visualization setup:
        private const DisplayFrameType DEFAULT_FRAME_TYPE = DisplayFrameType.InfraredOnly; // Set infrared frame as default frame source to visualize
        private DisplayFrameType currentDisplayFrameType;
        private DisplayFrameType previousDisplayFrameType;
        // IR and Depth Bitmaps
        private WriteableBitmap infrBitmap = null;
        private WriteableBitmap depthBitmap = null;

        private bool _addBody = false;

        // INFRARED NORMALIZATION VARIABLES:
        private const float infrDataScale = 6.0f;
        private const float infrMinVal = 0.01f;
        private const float infrMaxVal = 1.0f;
        private byte[] infrPixels;
        private uint infrBytesPerPixel;

        // DEPTH NORMALIZATION VARIABLES:
        private uint depthBytesPerPixel;
        private const int MapDepthToByte = 8000 / 256;
        private byte[] depthPixels = null;

        // BODYJOINTS VARIABLES:        
        private Body[] bodies;
        private CoordinateMapper coordinateMapper;
        private float[,] bodyJointsRecordings;

        // RECORDING VARIABLES DEFINITION:
        private RecordingStatus DEFAULT_RECORDING_STATUS = RecordingStatus.VisualizeOnly;
        private RecordingStatus currentRecordingStatus;

        private int framesToRecord = 60;
        // variables to store in a single array all the recorded frames
        private byte[,] InfrRecordings = null;
        private byte[,] depthRecordings = null;
        // counters
        private int infrCounter = 0;
        private int depthCounter = 0;
        private int bodyJointsCounter = 0;
        // variables used to covert from the recordings into images to save into disk
        public byte[] infrData = null;
        public byte[] depthData = null;

        private WriteableBitmap infrRecordingsBitmap = null;
        private WriteableBitmap depthRecordingsBitmap = null;

        private bool pauseFlag = false; // Variable used to stop counters

        // SAVING VARIABLES DEFINITION:
        // To create a directory
        private string path = null;
        private string savedRecordingsPath = null;
        private string time = null;

        // To define Recordings File Extesions
        private RecordingsFileExtension DEFAULT_RECORDINGS_FILE_EXTENSION = RecordingsFileExtension.PNG;
        private RecordingsFileExtension currentRecordingFileExtension;
        // Other variables:
        public int resizeRatio = 1;

        public MainWindow()
        {
            // Kinect Definition:
            this.kinect = KinectSensor.GetDefault();

            // Open kinect Reader:
            this.multiSourceFrameReader = kinect.OpenMultiSourceFrameReader(FrameSourceTypes.Infrared | FrameSourceTypes.Depth | FrameSourceTypes.Body);

            // wiring methods to handle FrameArrived events:
            multiSourceFrameReader.MultiSourceFrameArrived += MultiSourceFrameReader_MultiSourceFrameArrived;

            // Coordinate Mapper (this allow us to transform cartesian coordinates into screen pixels coordinates)
            this.coordinateMapper = kinect.CoordinateMapper;

            // Frame Description:
            this.infrFrameDescription = this.kinect.InfraredFrameSource.FrameDescription;
            this.depthFrameDescription = this.kinect.DepthFrameSource.FrameDescription;

            // arrays to store pixels before rendering 
            this.infrPixels = new byte[this.infrFrameDescription.Width * this.infrFrameDescription.Height];
            this.depthPixels = new byte[this.depthFrameDescription.Width * this.depthFrameDescription.Height];

            //// Bitmaps Definition:
            this.infrBitmap = new WriteableBitmap(infrFrameDescription.Width, infrFrameDescription.Height, 96.0, 96.0, PixelFormats.Gray8, null);
            this.depthBitmap = new WriteableBitmap(depthFrameDescription.Width, depthFrameDescription.Height, 96.0, 96.0, PixelFormats.Gray8, null);

            // Bitmaps for recordings
            this.infrRecordingsBitmap = new WriteableBitmap(infrFrameDescription.Width, infrFrameDescription.Height, 96.0, 96.0, PixelFormats.Gray8, null);
            this.depthRecordingsBitmap = new WriteableBitmap(depthFrameDescription.Width, depthFrameDescription.Height, 96.0, 96.0, PixelFormats.Gray8, null);

            // bytes per pixel
            this.depthBytesPerPixel = depthFrameDescription.BytesPerPixel;
            this.infrBytesPerPixel = infrFrameDescription.BytesPerPixel;

            // use the window object as the view model in this simple example
            this.DataContext = this;

            // open the sensor
            this.kinect.Open();

            InitializeComponent();

            InitialSetup();
            // Set initial frame to display:
            SetFrameDisplay(DEFAULT_FRAME_TYPE);
            // Set initial Recording Status:
            this.currentRecordingStatus = DEFAULT_RECORDING_STATUS;
            // Set initial Recording File Extension:
            this.currentRecordingFileExtension = DEFAULT_RECORDINGS_FILE_EXTENSION;


        }
        // INITIAL CONFIGURATION AND VALUES OF KAPTURE UI
        private void InitialSetup()
        {
            this.chboxInfrOnly.IsChecked = true;
            // set desktop as default path:
            this.path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            // set Default values for Path and Frames to Record
            this.tboxPath.Text = path;
            this.tboxFramesToRecord.Text = Convert.ToString(this.framesToRecord);
            // Disable Recording Settings Panel:
            this.stkSettings.IsEnabled = false;
            // Disable buttons of Modifycation Panel
            this.btnCancelSettings.IsEnabled = false;
            this.btnSaveSettings.IsEnabled = false;
            // SETTINGS PANEL BUTTONS AND CHECKBOXES
            // Set all the Streams to Record
            this.chboxDepth.IsChecked = true;
            this.chboxInfrared.IsChecked = true;
            this.chboxBodyJoints.IsChecked = true;
            // Set Png extension as default recording file type (only applies for depth and IR Data, Body Joints is always recorded into CSV Files)
            this.chboxPNG.IsChecked = true;
            // Set Control Panel buttons
            this.btnRecord.IsEnabled = true;
            this.btnPause.IsEnabled = false;
            this.btnCancel.IsEnabled = false;
            this.btnSave.IsEnabled = false;
            
            

            this.tboxLog.Text += "Welcome, Please connect a Kinect Sensor or run a .xef file from Kinect Studio ..." + Environment.NewLine;
        }


        private void SetFrameDisplay(DisplayFrameType _newDisplayFrameType)
        {
            currentDisplayFrameType = _newDisplayFrameType;
            string _previousContent;
            switch (currentDisplayFrameType)
            {
                case DisplayFrameType.DepthOnly:
                    _previousContent = this.tboxLog.Text;
                    tboxLog.Text += "Showing Depth Frames Only ... " + Environment.NewLine;
                    tboxLog.ScrollToEnd();
                    break;
                case DisplayFrameType.InfraredOnly:
                    _previousContent = this.tboxLog.Text;
                    tboxLog.Text += "Showing Infrared Frames Only ..." + Environment.NewLine;
                    tboxLog.ScrollToEnd();
                    break;
                case DisplayFrameType.BodyJointsOnly:
                    _previousContent = this.tboxLog.Text;
                    tboxLog.Text += "Showing Body Joints Only ..." + Environment.NewLine;
                    tboxLog.ScrollToEnd();
                    break;
                case DisplayFrameType.BodyJointsAdd:
                    _previousContent = this.tboxLog.Text;
                    tboxLog.Text += "Showing Body Joints Add ..." + Environment.NewLine;
                    tboxLog.ScrollToEnd();
                    break;
                default:
                    break;

            }
        }
        private void MultiSourceFrameReader_MultiSourceFrameArrived(object sender, MultiSourceFrameArrivedEventArgs e)
        {
            MultiSourceFrame _multiSourceFrame = e.FrameReference.AcquireFrame();

            if (_multiSourceFrame == null)
            {
                this.tboxLog.Text += "No Frames received from the sensor ... " + Environment.NewLine;
                return;
            }
            
            switch (currentRecordingStatus)
            {
                case RecordingStatus.VisualizeOnly:
                    
                    switch (currentDisplayFrameType)
                    {
                        case DisplayFrameType.InfraredOnly:
                            this.previousDisplayFrameType = DisplayFrameType.InfraredOnly;
                            using (InfraredFrame _infrFrame = _multiSourceFrame.InfraredFrameReference.AcquireFrame())
                            {
                                bool show = true;
                                bool record = false;
                                imgCanvas.Children.Clear();
                                ShowIrFrame(_infrFrame, show, record);
                            }
                            if(_addBody == true)
                            {
                                using (BodyFrame _bodyframe = _multiSourceFrame.BodyFrameReference.AcquireFrame())
                                {
                                    bool show = true;
                                    bool record = false;
                                    //imgFrameDisplay.Source = null;
                                    ShowBodyJoints(_bodyframe, show, record);
                                }
                            }
                            break;
                        case DisplayFrameType.DepthOnly:
                            this.previousDisplayFrameType = DisplayFrameType.DepthOnly;
                            using (DepthFrame _depthFrame = _multiSourceFrame.DepthFrameReference.AcquireFrame())
                            {
                                bool show = true;
                                bool record = false;
                                imgCanvas.Children.Clear();
                                ShowDepthFrame(_depthFrame, show, record);
                            }
                            if (_addBody == true)
                            {
                                using (BodyFrame _bodyframe = _multiSourceFrame.BodyFrameReference.AcquireFrame())
                                {
                                    bool show = true;
                                    bool record = false;
                                    //imgFrameDisplay.Source = null;
                                    ShowBodyJoints(_bodyframe, show, record);
                                }
                            }
                            break;
                        case DisplayFrameType.BodyJointsOnly:
                            this.previousDisplayFrameType = DisplayFrameType.BodyJointsOnly;
                            using (BodyFrame _bodyframe = _multiSourceFrame.BodyFrameReference.AcquireFrame())
                            {
                                bool show = true;
                                bool record = false;
                                imgFrameDisplay.Source = null;
                                ShowBodyJoints(_bodyframe, show, record);
                            }
                            break;
                        case DisplayFrameType.BodyJointsAdd:
                            //SetFrameDisplay(previousDisplayFrameType);
                            _addBody = true;
                            currentDisplayFrameType = previousDisplayFrameType;
                            break;                            
                        default:
                            break;
                    }
                    break;
                case RecordingStatus.Recording:
                    this.tboxLog.Text = "RECORDING ..." + Environment.NewLine;
                    // Visualize and Start to Record Data
                    switch (currentDisplayFrameType)
                    {
                        case DisplayFrameType.InfraredOnly:
                            using (InfraredFrame _infrFrame = _multiSourceFrame.InfraredFrameReference.AcquireFrame())
                            {
                                bool show = true;
                                bool record = true;
                                imgCanvas.Children.Clear();
                                ShowIrFrame(_infrFrame, show, record);
                            }
                            using(DepthFrame _depthFrame = _multiSourceFrame.DepthFrameReference.AcquireFrame())
                            {
                                bool show = false;
                                bool record = true;
                                imgCanvas.Children.Clear();
                                ShowDepthFrame(_depthFrame, show, record);
                            }
                            using (BodyFrame _bodyframe = _multiSourceFrame.BodyFrameReference.AcquireFrame())
                            {
                                
                                bool show = false;
                                if (_addBody == true)
                                {
                                    show = true;
                                }
                                bool record = true;
                                //imgFrameDisplay.Source = null;
                                ShowBodyJoints(_bodyframe, show, record);
                            }
                            //this.tboxLog.Text = "Recordings Done ...";
                            
                            break;
                        case DisplayFrameType.DepthOnly:
                            using (DepthFrame _depthFrame = _multiSourceFrame.DepthFrameReference.AcquireFrame())
                            {
                                bool show = true;
                                bool record = true;

                                ShowDepthFrame(_depthFrame, show, record);
                            }
                            using (InfraredFrame _infrFrame = _multiSourceFrame.InfraredFrameReference.AcquireFrame())
                            {
                                bool show = false;
                                bool record = true;
                                imgCanvas.Children.Clear();
                                ShowIrFrame(_infrFrame, show, record);
                            }
                            using (BodyFrame _bodyframe = _multiSourceFrame.BodyFrameReference.AcquireFrame())
                            {

                                bool show = false;
                                if (_addBody == true)
                                {
                                    show = true;
                                }
                                bool record = true;
                                //imgFrameDisplay.Source = null;
                                ShowBodyJoints(_bodyframe, show, record);
                            }
                            break;
                        case DisplayFrameType.BodyJointsOnly:
                            using (DepthFrame _depthFrame = _multiSourceFrame.DepthFrameReference.AcquireFrame())
                            {
                                bool show = false;
                                bool record = true;

                                ShowDepthFrame(_depthFrame, show, record);
                            }
                            using (InfraredFrame _infrFrame = _multiSourceFrame.InfraredFrameReference.AcquireFrame())
                            {
                                bool show = false;
                                bool record = true;
                                imgCanvas.Children.Clear();
                                ShowIrFrame(_infrFrame, show, record);
                            }
                            using (BodyFrame _bodyframe = _multiSourceFrame.BodyFrameReference.AcquireFrame())
                            {

                                bool show = false;
                                if (_addBody == true)
                                {
                                    show = true;
                                }
                                bool record = true;
                                //imgFrameDisplay.Source = null;
                                ShowBodyJoints(_bodyframe, show, record);
                            }
                            break;
                        case DisplayFrameType.BodyJointsAdd:
                            _addBody = true;
                            currentDisplayFrameType = previousDisplayFrameType;
                            break;
                        default:
                            break;
                    }
                    break;
                case RecordingStatus.Cancel:
                    this.tboxLog.Text += "RECORDINGS CANCELLED !!!! " + Environment.NewLine;
                    this.currentRecordingStatus = RecordingStatus.VisualizeOnly;
                    break;
                
                default:
                    break;
            }
            
        }
        // METHODS FOR NORMALIZATION AND VISUALIZATION OF DATA RECEIVED FROM THE KINECT SENSOR:
        private void ShowBodyJoints(BodyFrame _bodyFrame, bool _show, bool _record)
        {
            bool _bodyJointsReceived = false;

            if(_bodyFrame != null)
            {
                if(bodies == null)
                {
                    bodies = new Body[_bodyFrame.BodyCount];
                }
                _bodyFrame.GetAndRefreshBodyData(bodies);
                _bodyJointsReceived = true;
            }

            if (_bodyJointsReceived)
            {
                imgCanvas.Children.Clear();
                int _bodiesIndex = 0;
                foreach (Body body in bodies.Where(b => b.IsTracked))
                {
                    int _jointIndex = 0;
                    foreach(var joint in body.Joints)
                    {
                        CameraSpacePoint position = joint.Value.Position;

                        if(position.Z < 0)
                        {
                            position.Z = 0.1f;
                        }

                        if(_record == true && bodyJointsCounter < framesToRecord)
                        {
                            this.bodyJointsRecordings[this.bodyJointsCounter, _bodiesIndex + _jointIndex] = position.X;
                            this.bodyJointsRecordings[this.bodyJointsCounter, _bodiesIndex + _jointIndex + 1] = position.Y;
                            this.bodyJointsRecordings[this.bodyJointsCounter, _bodiesIndex + _jointIndex + 2 ] = position.Z;

                            _jointIndex = _jointIndex + 3;
                        }
                        if(_show == true)
                        {
                            DepthSpacePoint _depthSpacePoint = coordinateMapper.MapCameraPointToDepthSpace(position);
                            if (joint.Value.TrackingState == TrackingState.Tracked)
                            {
                                DrawJoint(new System.Windows.Point(_depthSpacePoint.X, _depthSpacePoint.Y), new SolidColorBrush(Colors.Purple));
                            }
                        }
                         
                    }
                    _bodiesIndex = _bodiesIndex + 25;
                }
            }
            if(_bodyJointsReceived==true && _record == true && bodyJointsCounter<framesToRecord && this.pauseFlag==false)
            {
                this.bodyJointsCounter++;
                
            }
            if (this.chboxBodyJoints.IsChecked == true && this.bodyJointsCounter >= framesToRecord)
            {
                this.tboxLog.Text += "Body Joints ready" + Environment.NewLine;
                this.btnSave.IsEnabled = true;
            }
        }

        private void DrawJoint(System.Windows.Point jointCoord, SolidColorBrush s)
        {
            if (jointCoord.X < 0 || jointCoord.Y < 0)
            {
                return;
            }
            Ellipse ellipse = new Ellipse()
            {
                Width = 5,
                Height = 5,
                Fill = s
            };
            Canvas.SetLeft(ellipse, (jointCoord.X / depthFrameDescription.Width) * imgCanvas.ActualWidth - ellipse.Width / 2);
            Canvas.SetTop(ellipse, (jointCoord.Y / depthFrameDescription.Height) * imgCanvas.ActualHeight - ellipse.Height / 2);
            imgCanvas.Children.Add(ellipse);
        }

        private void ShowDepthFrame(DepthFrame _depthFrame, bool _show, bool _record)
        {
            bool _depthFrameProcessed = false; // this variable acts like a trigger when the depthFrameProcessed method is finished

            if(_depthFrame != null)
            {
                using(KinectBuffer _depthBuffer = _depthFrame.LockImageBuffer())
                {
                    if((_depthFrame.FrameDescription.Width * _depthFrame.FrameDescription.Height) == (_depthBuffer.Size / _depthFrame.FrameDescription.BytesPerPixel))
                    {
                        ushort _maxDepth = ushort.MaxValue;

                        ProcessDepthFrameData(_depthBuffer.UnderlyingBuffer, _depthBuffer.Size, _depthFrame.DepthMinReliableDistance, _maxDepth);
                        _depthFrameProcessed = true;
                    }
                    if(_depthFrameProcessed==true && _record == true && depthCounter<framesToRecord && this.pauseFlag == false)
                    {
                        this.depthCounter++;                        
                    }
                    if (_depthFrameProcessed && _show == true)
                    {
                        this.depthBitmap.WritePixels(new Int32Rect(0, 0, this.depthBitmap.PixelWidth, depthBitmap.PixelHeight),
                        this.depthPixels, this.depthBitmap.PixelWidth, 0);
                                                
                        this.imgFrameDisplay.Source = depthBitmap; 
                    }
                }
            }
            if (this.chboxDepth.IsChecked == true && this.depthCounter >= framesToRecord)
            {
                this.tboxLog.Text += "Depth Data Ready ..." + Environment.NewLine;
                this.btnSave.IsEnabled = true;
            }
        }
        private unsafe void ProcessDepthFrameData(IntPtr depthFrameData, uint depthFrameDataSize, ushort minDepth, ushort maxDepth)
        {
            ushort* framedata = (ushort*)depthFrameData;

            for (int i = 0; i < (int)(depthFrameDataSize / this.depthFrameDescription.BytesPerPixel); ++i)
            {
                ushort depth = framedata[i];
                this.depthPixels[i] = (byte)(depth >= minDepth && depth <= maxDepth ? (depth / MapDepthToByte) : 0);

                if (this.currentRecordingStatus == RecordingStatus.Recording && this.depthCounter < this.framesToRecord)
                {
                    this.depthRecordings[this.depthCounter, i] = depthPixels[i];
                }
            }
        }

        private void ShowIrFrame(InfraredFrame _infrFrame, bool _show, bool _record)
        {
            bool _infrFrameProcessed = false; // this variable acts like a trigger when the infraredFrameProcessed method is finished

            if (_infrFrame != null)
            {
                using (KinectBuffer _infrBuffer = _infrFrame.LockImageBuffer())
                {
                    if ((_infrFrame.FrameDescription.Width * _infrFrame.FrameDescription.Height) == (_infrBuffer.Size / _infrFrame.FrameDescription.BytesPerPixel))
                    {
                        ProcessInfrFrameData(_infrBuffer.UnderlyingBuffer, _infrBuffer.Size, _infrFrame.FrameDescription.BytesPerPixel);
                        _infrFrameProcessed = true;
                    }
                    // Counter will increase until frames to record value
                    if(_infrFrameProcessed==true && _record == true && infrCounter<framesToRecord && this.pauseFlag == false)
                    {
                        this.infrCounter++;
                        
                    }
                    // Frame processed, lets render it
                    if (_infrFrameProcessed == true && _show == true)
                    {
                        this.infrBitmap.WritePixels(new Int32Rect(0, 0, this.infrBitmap.PixelWidth, infrBitmap.PixelHeight),
                        this.infrPixels, this.infrBitmap.PixelWidth, 0);

                        this.imgFrameDisplay.Source = infrBitmap;
                    }

                    if (this.chboxInfrared.IsChecked == true && this.infrCounter >= framesToRecord)
                    {
                        this.tboxLog.Text += "Infrared Recordings ready to store ..." + Environment.NewLine;
                        this.btnSave.IsEnabled = true;
                    }
                }
            }
        }
        // Process Infrared Data(this must be done in unsafe mode):
        private unsafe void ProcessInfrFrameData(IntPtr infraredFrameData, uint infraredFrameDataSize, uint bytesPerPix)
        {
            ushort* irframeData = (ushort*)infraredFrameData;

            float* backBuffer = (float*)this.infrBitmap.BackBuffer;

            for (int i = 0; i < (int)(infraredFrameDataSize / bytesPerPix); ++i)
            {
                // normalization of the infrared values:
                ushort irValue = irframeData[i];

                float irRange = (float)(irValue - ushort.MinValue) / (float)(ushort.MaxValue - ushort.MinValue);

                float ir_desiredRange = (float)(irRange * (1 - infrMinVal)) + infrMinVal;
                float ir_desiredRange_withBrithtness = ir_desiredRange * infrDataScale;

                infrPixels[i] = (byte)((Math.Min(infrMaxVal, ir_desiredRange_withBrithtness)) * 256);

                if (this.currentRecordingStatus == RecordingStatus.Recording && this.infrCounter < this.framesToRecord)
                {
                    this.InfrRecordings[this.infrCounter, i] = infrPixels[i];
                }
            }
        }
        
        // VISUALIZATION CHECHBOXES METHODS
        private void chboxInfrOnly_Click(object sender, RoutedEventArgs e)
        {
            this.chboxDepthOnly.IsChecked = false;
            this.chboxBodyJointsOnly.IsChecked = false;
            SetFrameDisplay(DisplayFrameType.InfraredOnly);
        }
        private void chboxDepthOnly_Click(object sender, RoutedEventArgs e)
        {
            this.chboxInfrOnly.IsChecked = false;
            this.chboxBodyJointsOnly.IsChecked = false;
            SetFrameDisplay(DisplayFrameType.DepthOnly);
        }
        private void BodyJoints_Click(object sender, RoutedEventArgs e)
        {
            this.chboxDepthOnly.IsChecked = false;
            this.chboxInfrOnly.IsChecked = false;
            this.chboxBodyJointsAdd.IsChecked = false;
            SetFrameDisplay(DisplayFrameType.BodyJointsOnly);
        }
        private void BodyJointsAdd_Click(object sender, RoutedEventArgs e)
        {
            this.chboxBodyJointsOnly.IsChecked = false;
            this.previousDisplayFrameType = currentDisplayFrameType;
            SetFrameDisplay(DisplayFrameType.BodyJointsAdd);
        }


        // it is necessary to release all the resources used by the kinect before closing Kapture App
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            // Dispose all the frames:
            if (this.multiSourceFrameReader != null)
            {
                this.multiSourceFrameReader.Dispose();
                this.multiSourceFrameReader = null;
            }

            if (this.kinect != null)
            {
                this.kinect.Close();
                this.kinect = null;
            }
        }

        private void btnRecord_Click(object sender, RoutedEventArgs e)
        {
            this.infrCounter = 0;
            this.depthCounter = 0;
            this.bodyJointsCounter = 0;
            this.InfrRecordings = new byte[framesToRecord, this.infrFrameDescription.Width * this.infrFrameDescription.Height];
            this.depthRecordings = new byte[framesToRecord, this.depthFrameDescription.Width * this.depthFrameDescription.Height];
            this.bodyJointsRecordings = new float[framesToRecord, (6 * 25 * 3)]; // 6 bodies, 25 joints, x,y,z coordinates for each joint

            this.currentRecordingStatus = RecordingStatus.Recording;
            this.btnCancel.IsEnabled = true;
            this.btnPause.IsEnabled = true;
            this.btnRecord.IsEnabled = false;
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {

            // Disable record and Cancel button during Saving 
            this.btnRecord.IsEnabled = false;
            this.btnPause.IsEnabled = false;
            this.btnCancel.IsEnabled = false;


            // Create a directory to store data from IR, Depth and Skeleton Streams
            //this.tboxLog.Text = "SAVING DATA, DO NOT CLOSE THE APP !!! ... " + Environment.NewLine;
            this.tboxLog.Text += "Creating directories ... " + Environment.NewLine;
            CreateSavingDirectory(path);
            this.tboxLog.Text += "Saving Data into Disk, Please Wait ..." + Environment.NewLine;

            //System.Threading.Thread.Sleep(1000);

            // Save Selected Recordings:
            if (this.chboxInfrared.IsChecked == true && this.btnRecord.IsEnabled==false)
            {
                SaveInfraredFrames();
            }
            if(this.chboxDepth.IsChecked == true && this.btnRecord.IsEnabled == false)
            {
                SaveDepthFrames();
            }            
            if(this.chboxBodyJoints.IsChecked == true && this.btnRecord.IsEnabled == false)
            {
                SaveBodyJoints();
            }
            tboxLog.Text += "SAVING PROCESS COMPLETE ... " + Environment.NewLine;
            // enable Record Button 
            this.btnRecord.IsEnabled = true;
            this.btnSave.IsEnabled = false;

        }
        private void CreateSavingDirectory(string _path)
        {
            this.time = System.DateTime.Now.ToString("yyyy-MM-dd hh-mm-ss");
            this.savedRecordingsPath = System.IO.Path.Combine(_path, "KData" + time);
            // string variables to cast into the path according to the choosen File Extension
            string _CSVExtension = "";
            string _PNGEtension = "";
            string _BMPExtension = "";
            
            // Create Directory 
            if (!Directory.Exists(savedRecordingsPath))
            {
                Directory.CreateDirectory(savedRecordingsPath);
                // Create IR, Depth and Skeleton subfolders:

                if(this.chboxCSV.IsChecked == true)
                {
                    _CSVExtension = " CSV ";
                    Directory.CreateDirectory(System.IO.Path.Combine(savedRecordingsPath, "Depth" + _CSVExtension));
                    Directory.CreateDirectory(System.IO.Path.Combine(savedRecordingsPath, "Infrared"+ _CSVExtension));
                    Directory.CreateDirectory(System.IO.Path.Combine(savedRecordingsPath, "Body Joints"));
                }
                if (this.chboxPNG.IsChecked == true)
                {
                    _PNGEtension = " PNG ";
                    Directory.CreateDirectory(System.IO.Path.Combine(savedRecordingsPath, "Depth" + _PNGEtension));
                    Directory.CreateDirectory(System.IO.Path.Combine(savedRecordingsPath, "Infrared" + _PNGEtension));
                    Directory.CreateDirectory(System.IO.Path.Combine(savedRecordingsPath, "Body Joints"));
                }
                if (this.chboxBMP.IsChecked == true)
                {
                    _BMPExtension = " BMP ";
                    Directory.CreateDirectory(System.IO.Path.Combine(savedRecordingsPath, "Depth" + _BMPExtension));
                    Directory.CreateDirectory(System.IO.Path.Combine(savedRecordingsPath, "Infrared" + _BMPExtension));
                    Directory.CreateDirectory(System.IO.Path.Combine(savedRecordingsPath, "Body Joints"));
                }

            }
        }
        // METHODS TO SAVE RECORDED FRAMES:
       
        
        private void SaveDepthFrames()
        {
            this.depthData = new byte[this.depthFrameDescription.Width * this.depthFrameDescription.Height];

            for (int count = 0; count < framesToRecord; count++)
            {
                // Read corresponding recordings:

                for (int j = 0; j < (this.depthFrameDescription.Width * this.depthFrameDescription.Height); j++)
                {
                    this.depthData[j] = this.depthRecordings[count, j];
                }
                this.depthRecordingsBitmap.WritePixels(new Int32Rect(0, 0, this.depthRecordingsBitmap.PixelWidth, depthRecordingsBitmap.PixelHeight),
                    this.depthData, this.depthRecordingsBitmap.PixelWidth, 0);
                // save to disk:
                // PNG OR BMP extensions selected
                if (this.chboxPNG.IsChecked == true || this.chboxBMP.IsChecked == true)
                {
                    string _PNGExtension = "";
                    string _BMPExtension = "";
                    // Set corresponding file extensions:

                    if (this.depthRecordingsBitmap != null)
                    {
                                              

                        BitmapEncoder depthEncoderPNG = new PngBitmapEncoder();
                        depthEncoderPNG.Frames.Add(BitmapFrame.Create(this.depthRecordingsBitmap));
                       
                        BitmapEncoder depthEncoderBMP = new BmpBitmapEncoder();
                        depthEncoderBMP.Frames.Add(BitmapFrame.Create(this.depthRecordingsBitmap));

                        string time = System.DateTime.Now.ToString("hh-mm-ss");
                        string _path = null;
                        string _infrpath = null;

                        if (this.chboxBMP.IsChecked == true)
                        {
                            _BMPExtension = " BMP ";
                            _path = System.IO.Path.Combine(this.savedRecordingsPath, "Depth" + _BMPExtension);
                            _infrpath = System.IO.Path.Combine(_path, "Depth " + count + " - " + time + ".bmp");
                            try
                            {
                                using (FileStream fs = new FileStream(_infrpath, FileMode.Create))
                                {
                                    depthEncoderBMP.Save(fs);
                                }
                            }
                            catch (IOException)
                            {

                            }
                        }
                        if (this.chboxPNG.IsChecked == true)
                        {
                            _PNGExtension = " PNG ";
                            _path = System.IO.Path.Combine(this.savedRecordingsPath, "depth" + _PNGExtension);
                            _infrpath = System.IO.Path.Combine(_path, "Depth " + count + " - " + time + ".png");
                            try
                            {
                                using (FileStream fs = new FileStream(_infrpath, FileMode.Create))
                                {
                                    depthEncoderPNG.Save(fs);
                                }
                            }
                            catch (IOException)
                            {

                            }
                        }
                    }
                }
                
            }
            if (this.chboxCSV.IsChecked == true)
            {
                if (this.depthRecordings != null)
                {
                    string time = System.DateTime.Now.ToString("hh-mm-ss");
                    string path = System.IO.Path.Combine(this.savedRecordingsPath, "Depth CSV");
                    string depthCSVPath = System.IO.Path.Combine(path, "Depth " + " - " + time + ".csv");
                    try
                    {
                        using (StreamWriter sw = new StreamWriter(depthCSVPath))
                        {

                            // It is necessary to swich rows and columns because csv files has a maximun size of approx 16000 columns
                            // and the lenght of each frame is bigger than that
                            int[,] csvHolder = new int[this.depthFrameDescription.Width * this.depthFrameDescription.Height, framesToRecord];
                            for (int row = 0; row < framesToRecord; row++)
                            {
                                for (int col = 0; col < this.depthFrameDescription.Width * depthFrameDescription.Height; col++)
                                {
                                    csvHolder[col, row] = this.depthRecordings[row, col];
                                    //sw.Write(this.csvHolder[row, col] + ",");
                                }
                                //sw.WriteLine();
                            }

                            for (int row = 0; row < (this.depthFrameDescription.Width * this.depthFrameDescription.Height); row++)
                            {
                                for (int col = 0; col < framesToRecord; col++)
                                {
                                    sw.Write(csvHolder[row, col] + ",");
                                }
                                sw.WriteLine();
                            }

                        }
                    }
                    catch (IOException)
                    {

                    }
                }
            }

            this.tboxLog.Text += "Depth Data Saved into Disk ... " + Environment.NewLine;
        }

        private void SaveInfraredFrames()
        {
            this.infrData = new byte[this.infrFrameDescription.Width * this.infrFrameDescription.Height];
            for (int ircount = 0; ircount < framesToRecord; ircount++)
            {
                // Read corresponding recordings:

                for (int jj = 0; jj < (this.infrFrameDescription.Width * this.infrFrameDescription.Height); jj++)
                {
                    this.infrData[jj] = this.InfrRecordings[ircount, jj];
                }
                // PNG OR BMP extensions selected
                if(this.chboxPNG.IsChecked == true || this.chboxBMP.IsChecked == true)
                {
                    string _PNGExtension = "";
                    string _BMPExtension = "";
                    // Set corresponding file extensions:
                    
                    this.infrRecordingsBitmap.WritePixels(new Int32Rect(0, 0, this.infrRecordingsBitmap.PixelWidth, infrRecordingsBitmap.PixelHeight),
                    this.infrData, (this.infrRecordingsBitmap.PixelWidth), 0);
                    // save to disk:
                    if (this.infrRecordingsBitmap != null)
                    {
                        BitmapEncoder infrEncoderPNG = new PngBitmapEncoder();
                        infrEncoderPNG.Frames.Add(BitmapFrame.Create(this.infrRecordingsBitmap));
                        BitmapEncoder infrEncoderBMP = new BmpBitmapEncoder();
                        infrEncoderBMP.Frames.Add(BitmapFrame.Create(this.infrRecordingsBitmap));


                        string time = System.DateTime.Now.ToString("hh-mm-ss");
                        string _path = null;
                        string _infrpath = null;
                        if (this.chboxBMP.IsChecked == true)
                        {
                            _BMPExtension = " BMP ";
                            _path = System.IO.Path.Combine(this.savedRecordingsPath, "Infrared" + _BMPExtension);
                            _infrpath = System.IO.Path.Combine(_path, "IRData " + ircount + " - " + time + ".bmp");
                            try
                            {
                                using (FileStream fs = new FileStream(_infrpath, FileMode.Create))
                                {
                                    infrEncoderBMP.Save(fs);
                                }
                            }
                            catch (IOException)
                            {

                            }
                        }
                        if(this.chboxPNG.IsChecked == true)
                        {
                            _PNGExtension = " PNG ";
                            _path = System.IO.Path.Combine(this.savedRecordingsPath, "Infrared" + _PNGExtension);
                            _infrpath = System.IO.Path.Combine(_path, "IRData " + ircount + " - " + time + ".png");
                            try
                            {
                                using (FileStream fs = new FileStream(_infrpath, FileMode.Create))
                                {
                                    infrEncoderPNG.Save(fs);
                                }
                            }
                            catch (IOException)
                            {

                            }
                        }
                        
                    }
                }
                
                
            }
            if (this.chboxCSV.IsChecked == true)
            {
                if (this.InfrRecordings != null)
                {
                    string time = System.DateTime.Now.ToString("hh-mm-ss");
                    string path = System.IO.Path.Combine(this.savedRecordingsPath, "Infrared CSV");
                    string infrCSVPath = System.IO.Path.Combine(path, "Infrared " + " - " + time + ".csv");
                    try
                    {
                        using (StreamWriter sw = new StreamWriter(infrCSVPath))
                        {

                            // It is necessary to swich rows and columns because csv files has a maximun size of approx 16000 columns
                            // and the lenght of each frame is bigger than that
                            int[,] csvHolder = new int[this.infrFrameDescription.Width * this.infrFrameDescription.Height, framesToRecord];
                            for (int row = 0; row < framesToRecord; row++)
                            {
                                for (int col = 0; col < this.infrFrameDescription.Width * infrFrameDescription.Height; col++)
                                {
                                    csvHolder[col, row] = this.InfrRecordings[row, col];
                                    //sw.Write(this.csvHolder[row, col] + ",");
                                }
                                //sw.WriteLine();
                            }

                            for (int row = 0; row < (this.infrFrameDescription.Width * this.infrFrameDescription.Height) ; row++)
                            {
                                for (int col = 0; col < framesToRecord; col++)
                                {
                                    sw.Write(csvHolder[row, col] + ",");
                                }
                                sw.WriteLine();
                            }
                            
                        }
                    }
                    catch (IOException)
                    {

                    }
                }
            }
            this.tboxLog.Text += "Infrared Data Saved into Disk" + Environment.NewLine;
        }
        private void SaveBodyJoints()
        {
            if (this.bodyJointsRecordings != null)
            {
                string time = System.DateTime.Now.ToString("hh-mm-ss");
                string path = System.IO.Path.Combine(this.savedRecordingsPath, "Body Joints");
                string bodyJointsPath = System.IO.Path.Combine(path, "BodyJoints " + " - " + time + ".csv");
                try
                {
                    using (StreamWriter sw = new StreamWriter(bodyJointsPath))
                    {
                        for (int row = 0; row < framesToRecord; row++)
                        {
                            for (int col = 0; col < 6 * 25 * 3; col++)
                            {
                                sw.Write(this.bodyJointsRecordings[row, col] + ",");
                            }
                            sw.WriteLine();
                        }
                    }
                }
                catch (IOException)
                {

                }
            }
            this.tboxLog.Text += "Body Joints Data Saved into Disk ..." + Environment.NewLine;
        }
        // METHODS TO HANDLE IMAGE RESIZING
        
            

            // METHODS TO HANDLE CHANGES AT THE RECORDING SETTINGS
            private void btnModify_Click(object sender, RoutedEventArgs e)
        {
            // Enable stack panel:
            this.stkSettings.IsEnabled = true;
            // Enable Cancel and Save buttons:
            this.btnCancelSettings.IsEnabled = true;
            this.btnSaveSettings.IsEnabled = true;
            this.btnModify.IsEnabled = false;
            this.btnRecord.IsEnabled = false;
           
        }
        // In order to use folder dialog, it is necessary to include System.windows.forms at the project referece
        private void btnChange_Click(object sender, RoutedEventArgs e)
        {
            using (var folderDialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                folderDialog.SelectedPath = path;
                System.Windows.Forms.DialogResult result = folderDialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    this.path = folderDialog.SelectedPath;
                    this.tboxPath.Text = path;
                }
            }
           
        }
        
        
        private void btnCancelSettings_Click(object sender, RoutedEventArgs e)
        {
            // Disable stack panel:
            this.stkSettings.IsEnabled = false;
            // set default values 
            this.framesToRecord = Convert.ToInt32(framesToRecord);
            this.tboxPath.Text = this.path;

            // disable settings panel
            this.stkSettings.IsEnabled = false;

            // disable settings buttons
            this.btnCancelSettings.IsEnabled = false;
            this.btnModify.IsEnabled = true;
            this.btnSaveSettings.IsEnabled = false;
            // once the settings are saved allow to start recordings
            this.btnRecord.IsEnabled = true;

            
            

        }

        private void btnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            // read values from the tboxes
            this.framesToRecord = Convert.ToInt32(this.tboxFramesToRecord.Text);
            this.tboxPath.Text = this.path;

            // disable settings panel
            this.stkSettings.IsEnabled = false;

            // disable settings buttons
            this.btnCancelSettings.IsEnabled = false;
            this.btnModify.IsEnabled = true;
            this.btnSaveSettings.IsEnabled = false;
            // once the settings are saved allow to start recordings
            this.btnRecord.IsEnabled = true;

            
            

        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.currentRecordingStatus = RecordingStatus.Cancel;

            this.btnSave.IsEnabled = false;
            this.btnPause.IsEnabled = false;
            this.btnRecord.IsEnabled = true;
            this.btnCancel.IsEnabled = false;

            this.infrCounter = 0;
            this.depthCounter = 0;
            this.bodyJointsCounter = 0;
        }

        private void btnPause_Click(object sender, RoutedEventArgs e)
        {
            
            string _content = Convert.ToString( this.btnPause.Content);
            switch (_content)
            {
                case "Pause":
                    this.pauseFlag = true;
                    this.currentRecordingStatus = RecordingStatus.Recording;
                    this.btnPause.Content = "Resume";
                    this.tboxLog.Text += "RECORDING PAUSED: /n" + "Stored " + this.infrCounter + " of " + framesToRecord + Environment.NewLine;
                    break;
                case "Resume":
                    this.pauseFlag = false;
                    this.currentRecordingStatus = RecordingStatus.Recording;
                    this.btnPause.Content = "Pause";
                    this.tboxLog.Text += "RESUMING RECORDINGS ...";
                    break;
                default:
                    break;
            }
            
            //this.currentRecordingStatus = RecordingStatus.Pause;


            this.btnSave.IsEnabled = false;
            this.btnPause.IsEnabled = true;
            this.btnRecord.IsEnabled = false;
            this.btnCancel.IsEnabled = true;
        }

        
    }
}
