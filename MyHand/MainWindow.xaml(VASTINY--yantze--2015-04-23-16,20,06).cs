﻿using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
//using System.Windows.Documents;
//using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
//using System.Windows.Navigation;
using System.Windows.Shapes;

using Microsoft.Kinect;
using Microsoft.Kinect.Toolkit;
using Microsoft.Kinect.Toolkit.Controls;
using Microsoft.Kinect.Toolkit.Interaction;

//using SerialPortChat;
using System.IO.Ports;
using System.Threading;
using System.Net.Sockets;

//using Fizbin.Kinect.Gestures.Segments;
//using Fizbin.Kinect.Gestures;


//using Coding4Fun.Kinect.Wpf;
//using HandTrackingFramework;
//using Microsoft.Samples.Kinect.ControlsBasics

using LightBuzz.Vitruvius;
using LightBuzz.Vitruvius.WPF;

using KinectSimonSaysPoses;


namespace MyHand
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        #region varible init
        private int angleX;
        private int angleY;
        private int preAngleX;
        private int preAngleY;
        private bool _continue;
        private bool _wait;
        private string _debug;

        bool isBackGestureActive = false;
        bool isForwardGestureActive = false;

        // 串口通讯内容
        SerialPort portChat;
        TcpClient client;
        NetworkStream netPortChat;
        Thread portChatThread;
        Thread readPortChatThread;
        bool _useNetPort;

        private Pose startPose;
        private Pose[] poseLibrary;

        private readonly Brush[] skeletonBrushes = new Brush[] { Brushes.Blue };

        private Skeleton[] skeletons = new Skeleton[0];

        private KinectSensorChooser sensorChooser;

        // skeleton gesture recognizer
        private GestureController _gestureController;

        private struct JPoint
        {
            public float X;
            public float Y;
            public float Z;
        }

        private struct JAngle
        {
            public int X;
            public int Y;
        }
        
        private JPoint leftHand;
        private JPoint rightHand;

        private bool isLeftHand = false;

        int canvasWidth;
        int canvasHeight;

        #endregion
            
        public MainWindow()
        {
            InitializeComponent();

            // 启动串口线程
            _continue = true;
            _wait = false;
            _useNetPort = false;
            portChat = new SerialPort("COM3", 9600, Parity.None, 8);
            //client = new TcpClient("10.10.100.254", 8899);
            
            portChatThread = new Thread(sendServo);
            portChatThread.Start();
            //readPortChatThread = new Thread(readServo);
            //readPortChatThread.Start();
            

            // initialize the sensor chooser and UI
            this.sensorChooser = new KinectSensorChooser();
            this.sensorChooser.KinectChanged += SensorChooserOnKinectChanged;
            this.sensorChooserUi.KinectSensorChooser = this.sensorChooser;
            this.sensorChooser.Start();

            PopulatePoseLibrary();

            // Bind the sensor chooser's current sensor to the KinectRegion
            var regionSensorBinding = new Binding("Kinect") { Source = this.sensorChooser };
            BindingOperations.SetBinding(this.kinectRegion, KinectRegion.KinectSensorProperty, regionSensorBinding);

            _gestureController = new GestureController(GestureType.All);
            _gestureController.GestureRecognized += GestureController_GestureRecognized;

        }

        /// <summary>
        /// Called when the KinectSensorChooser gets a new sensor
        /// </summary>
        /// <param name="sender">sender of the event</param>
        /// <param name="args">event arguments</param>
        private void SensorChooserOnKinectChanged(object sender, KinectChangedEventArgs args)
        {
            if (args.OldSensor != null)
            {
                try
                {
                    args.OldSensor.DepthStream.Range = DepthRange.Default;
                    args.OldSensor.SkeletonStream.EnableTrackingInNearRange = false;
                    args.OldSensor.DepthStream.Disable();
                    args.OldSensor.SkeletonStream.Disable();
                }
                catch (InvalidOperationException)
                {
                    // KinectSensor might enter an invalid state while enabling/disabling streams or stream features.
                    // E.g.: sensor might be abruptly unplugged.
                }
            }

            if (args.NewSensor != null)
            {
                try
                {
                    args.NewSensor.DepthStream.Enable(DepthImageFormat.Resolution640x480Fps30);
                    //args.NewSensor.SkeletonStream.Enable();
                    args.NewSensor.SkeletonStream.Enable(new TransformSmoothParameters()
                    {
                        Smoothing = 0.75f,
                        Correction = 0.0f,
                        Prediction = 0.0f,
                        JitterRadius = 0.05f,
                        MaxDeviationRadius = 0.04f
                    });

                    try
                    {
                        args.NewSensor.DepthStream.Range = DepthRange.Near;
                        args.NewSensor.SkeletonStream.EnableTrackingInNearRange = true;
                    }
                    catch (InvalidOperationException)
                    {
                        // Non Kinect for Windows devices do not support Near mode, so reset back to default mode.
                        args.NewSensor.DepthStream.Range = DepthRange.Default;
                        args.NewSensor.SkeletonStream.EnableTrackingInNearRange = false;
                    }
                }
                catch (InvalidOperationException)
                {
                    // KinectSensor might enter an invalid state while enabling/disabling streams or stream features.
                    // E.g.: sensor might be abruptly unplugged.
                }


                //args.NewSensor.DepthFrameReady += OnDepthFrameReady;
                args.NewSensor.SkeletonFrameReady += OnSkeletonFrameReady;
            }
        }

        private void OnDepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
        {
            using (DepthImageFrame frame = e.OpenDepthImageFrame())
            {
                if (frame == null)
                    return;
                depthImage.Source = frame.ToBitmap(DepthImageMode.Colors);
                //depthImage.Source = frame.ToBitmapSource();                
            }
            //throw new NotImplementedException();
        }

        private void OnSkeletonFrameReady(object sender, SkeletonFrameReadyEventArgs e)
        {
            using (SkeletonFrame frame = e.OpenSkeletonFrame())
            {
                //skeletonCanvas.ClearSkeletons();
                if (frame == null)
                    return;

                // resize the skeletons array if needed
                if (skeletons.Length != frame.SkeletonArrayLength)
                    skeletons = new Skeleton[frame.SkeletonArrayLength];

                // get the skeleton data
                frame.CopySkeletonDataTo(skeletons);

                foreach (var skeleton in skeletons)
                {
                    // skip the skeleton if it is not being tracked 
                    if (skeleton.TrackingState != SkeletonTrackingState.Tracked)
                        continue;

                    //skeletonCanvas.DrawSkeleton(skeleton);

                    GetJointLocations(skeleton);
                    DrawSkeleton();

                    _gestureController.Update(skeleton);

                    //if (IsPose(skeleton, this.startPose))
                    //{
                    //    MessageBox.Show("you select start Pose");

                    //    // 这里可以改变仿生手势
                    //}

                    //// 是否双手放下
                    //if (IsPose(skeleton, this.poseLibrary[1]))
                    //{
                    //    MessageBox.Show("your Arms both down!!!");
                    //    _wait = true;
                    //}

                }
            }
        }

        void GestureController_GestureRecognized(object sender, GestureEventArgs e)
        {
            // Display the gesture type.
            gestureBlock.Text = e.Name;

            // Do something according to the type of the gesture.
            switch (e.Type)
            {
                case GestureType.JoinedHands:
                    break;
                case GestureType.Menu:
                    break;
                case GestureType.SwipeDown:
                    break;
                case GestureType.SwipeLeft:
                    break;
                case GestureType.SwipeRight:
                    break;
                case GestureType.SwipeLeftPPT:
                    // 切换PPT
                    MessageBox.Show("hahaha");
                    if (!isBackGestureActive && !isForwardGestureActive)
                    {
                        isBackGestureActive = true;
                        System.Windows.Forms.SendKeys.SendWait("{Left}");
                        MessageBox.Show("hahaha");
                    }
                    isBackGestureActive = false;
                    break;
                case GestureType.SwipeRightPPT:
                    if (!isBackGestureActive && !isForwardGestureActive)
                    {
                        isForwardGestureActive = true;
                        System.Windows.Forms.SendKeys.SendWait("{Right}");
                    }
                    isBackGestureActive = false;
                    break;
                case GestureType.SwipeUp:
                    break;
                case GestureType.WaveLeft:
                    break;
                case GestureType.WaveRight:
                    break;
                case GestureType.ZoomIn:
                    break;
                case GestureType.ZoomOut:
                    break;
                default:
                    break;
            }
        }


        private void GetJointLocations(Skeleton skeleton)
        {
            canvasWidth = (int)handCanvas.ActualWidth;
            canvasHeight = (int)handCanvas.ActualHeight;

            // 这里右手的结点是我左手的位置信息

            Joint center = skeleton.Joints[JointType.HipCenter];
            Joint left = skeleton.Joints[JointType.HandLeft];
            Joint right = skeleton.Joints[JointType.HandRight];
            leftHand = JointLocation(right);
            rightHand = JointLocation(left);
            //JAngle leftAngle = JointAngle(skeleton.Joints[JointType.HandLeft]);
            //gestureBlock.Text = "AX:" + leftAngle.X +"JX:"+ left.Position.X;

            //获取前面的那只手，好像这里的骨架完全相反的样子
            //Joint frontHand;
            JAngle fAHand;
            if (leftHand.Z < (rightHand.Z+ 0.1))
            {
                //frontHand = right;
                fAHand = JointAngle(right);
                isLeftHand = true;
            }
            else if (isLeftHand)
            {
                //frontHand = right;
                fAHand = JointAngle(right);
            }
            else
            {
                //frontHand = left;
                fAHand = JointAngle(left);
                isLeftHand = false;
            }


            //double X = Math.Atan(frontHand.Position.X / frontHand.Position.Z) / 3.14 * 180 + 90;
            //// 1.5 是程序放大的倍数, +90 是舵机范围0-180， +45 是前面的45度在手势区不可能到达
            //double Y = 90 - Math.Atan(frontHand.Position.Y / frontHand.Position.Z) / 3.14 * 180 + 45;
            //angleX = (int)X;
            //angleY = (int)Y;

            //// read hand 仿生手臂操作
            //double rX = 90 - Math.Atan(frontHand.Position.X / frontHand.Position.Z) / 3.14 * 180 * 2;
            //double rY = Math.Atan(frontHand.Position.Y / frontHand.Position.Z) / 3.14 * 180 * 1.5 + 90 + 45;

            angleX = 180 - fAHand.X;
            angleY = fAHand.Y + 30;
            
            gestureBlock.Text = "AX:" + angleX + " AY:" + angleY;
            //statusBarText.Text = _debug;
            debugBlock.Text = _debug;
            distBlock.Text = "距离摄像头:" + Math.Round(center.Position.Z, 2) + "米";
            //positionBlock.Text = "AY" + Y + "JY:" + left.Position.Y;

        }


        // 控制舵机角度
        private void sendServo()
        {
            //portChat.Open();

            try
            {
                if (_useNetPort)
                    netPortChat = client.GetStream();
                else
                    portChat.Open();
            }
            catch (System.IO.IOException e)
            {
                _debug = "COM未打开" + e.Message;
                _continue = false;
            }
            
            // 初始化等待计时器，5*sleeptime = 5 * 200 = 1s
            int _waitTime = 5;

            angleX = 0;
            angleY = 0;
            preAngleX = 0;
            preAngleY = 0;
            
            byte[] bytesToSend;
            while (_continue)
            {
                // Arduino延迟70ms,外加6ms读取(每个字节2ms)，20秒脉宽，共需要96ms.总延迟达到了344ms.
                Thread.Sleep(100);
                if (_wait || _waitTime > 1)
                {
                    --_waitTime;
                    continue;
                }
                else if (!_wait)
                    _waitTime = 5;

                //if (angleX > preAngleX + 1 || angleX < preAngleX - 1 ||
                //    angleY > preAngleY + 1 || angleY < preAngleY - 1)
                if (angleX != preAngleX || angleY != preAngleY)
                {

                    if (angleX < 0) angleX = 0;
                    else if (angleX > 127) angleX = 127;
                    if (angleY < 0) angleY = 0;
                    else if (angleY > 127) angleY = 127;

                    // 其中C9是控制位，十进制201，后面两个byte是范围为0-179的角度值
                    //bytesToSend = new byte[3] { 0xC9, (byte)angleX, (byte)angleY };
                    bytesToSend = new byte[3] { 115, (byte)angleX, (byte)angleY };
                    

                    // 确认使用无线串口还是有线串口
                    if (_useNetPort)
                        netPortChat.Write(bytesToSend, 0, bytesToSend.Length);
                    else
                        portChat.Write(bytesToSend, 0, bytesToSend.Length);
                    
                    preAngleX = angleX;
                    preAngleY = angleY;

                    //_debug = "舵机角度 X:" + angleX + " Y:" + angleY;
                    //_debug = DatePicker.

                    try
                    {
                        _debug = portChat.ReadLine();
                    }
                    catch (System.IO.IOException)
                    {
                        // 输入输出错误
                        _debug = "读取COM口IO问题";
                    }
                    catch (TimeoutException)
                    {
                        // 超时
                        _debug = "超时";
                    }
                    catch (Exception)
                    {
                        _debug = "无信息";
                    }
                }
            }
        }

        private void readServo()
        {
            while(_continue)
            {
                try
                {
                    _debug = portChat.ReadLine();
                }
                catch (System.IO.IOException)
                {
                    // 输入输出错误
                    _debug = "读取COM口IO问题";
                }
                catch (TimeoutException)
                {
                    // 超时
                    _debug = "超时";
                }
                catch (Exception)
                {
                    _debug = "无信息";
                }
                
            }
            
        }

        private void WindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _continue = false;
            this.portChatThread.Abort();
            //this.readPortChatThread.Abort();

            this.sensorChooser.Stop();

            if (_useNetPort)
            {
                netPortChat.Close();
                client.Close();
            }
        }

        #region Change Servo Angle
        private void PageLeftButtonClick(object sender, RoutedEventArgs e)
        {
            angleX += 5;
        }

        private void PageRightButtonClick(object sender, RoutedEventArgs e)
        {
            angleX -= 5;
        }

        private void PageUpButtonClick(object sender, RoutedEventArgs e)
        {
            angleY -= 5;
        }

        private void PageDownButtonClick(object sender, RoutedEventArgs e)
        {
            angleY += 5;
        }
        #endregion

        private void DrawSkeleton()
        {
            //SetElementPosition(LeftHandEllipse, leftHand);
            //SetElementPosition(RightHandEllipse, rightHand);
            SetElementPosition(LeftHandImage, rightHand);
            SetElementPosition(RightHandImage, leftHand);

        }

        private void SetElementPosition(FrameworkElement element, JPoint jp)
        {
            Canvas.SetLeft(element, jp.X - element.Width / 2);
            Canvas.SetTop(element, jp.Y);
            //Canvas.SetZIndex(element, 100);
        }



        private JPoint JointLocation(Joint joint)
        {
            JPoint tmp;
            var scaledJoint = joint.ScaleTo(canvasWidth, canvasHeight, .3f, .3f);
            tmp.X = scaledJoint.Position.X;
            tmp.Y = scaledJoint.Position.Y;
            tmp.Z = scaledJoint.Position.Z;
            return tmp;
        }

        private JAngle JointAngle(Joint joint)
        {
            JAngle tmp;
            var scaledJoint = joint.ScaleTo(canvasWidth, canvasHeight, .3f, .3f);

            // 空间坐标系的单位是米,所以先乘以100变到厘米
            float x = joint.Position.X * 100 + 90;
            float y = 90 - (joint.Position.Y * 100);
            tmp.X = (int)Math.Round(x);
            tmp.Y = (int)Math.Round(y);
            return tmp;
        }

        private void realHandOnClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("You clicked button Me!");
        }

        private void interHandOnClick(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("You clicked button Me!");
        }

        
        private static Point GetJointPoint(KinectSensor kinectDevice, Joint joint, Size containerSize, Point offset)
        {
            DepthImagePoint point;

            point = kinectDevice.CoordinateMapper.MapSkeletonPointToDepthPoint(joint.Position, kinectDevice.DepthStream.Format);

            
            
            point.X = (int)((point.X * containerSize.Width / kinectDevice.DepthStream.FrameWidth) - offset.X);
            point.Y = (int)((point.Y * containerSize.Height / kinectDevice.DepthStream.FrameHeight) - offset.Y);

            return new Point(point.X, point.Y);
        }

        private void PopulatePoseLibrary()
        {
            this.poseLibrary = new Pose[4];

            //游戏开始 Pose - 伸开双臂 Arms Extended
            this.startPose = new Pose();
            this.startPose.Title = "Start Pose";
            this.startPose.Angles = new PoseAngle[4];
            this.startPose.Angles[0] = new PoseAngle(JointType.ShoulderLeft, JointType.ElbowLeft, 180, 20);
            this.startPose.Angles[1] = new PoseAngle(JointType.ElbowLeft, JointType.WristLeft, 180, 20);
            this.startPose.Angles[2] = new PoseAngle(JointType.ShoulderRight, JointType.ElbowRight, 0, 20);
            this.startPose.Angles[3] = new PoseAngle(JointType.ElbowRight, JointType.WristRight, 0, 20);


            //Pose 1 -举起手来 Both Hands Up
            this.poseLibrary[0] = new Pose();
            this.poseLibrary[0].Title = "举起手来(Arms Up)";
            this.poseLibrary[0].Angles = new PoseAngle[4];
            this.poseLibrary[0].Angles[0] = new PoseAngle(JointType.ShoulderLeft, JointType.ElbowLeft, 180, 20);
            this.poseLibrary[0].Angles[1] = new PoseAngle(JointType.ElbowLeft, JointType.WristLeft, 90, 20);
            this.poseLibrary[0].Angles[2] = new PoseAngle(JointType.ShoulderRight, JointType.ElbowRight, 0, 20);
            this.poseLibrary[0].Angles[3] = new PoseAngle(JointType.ElbowRight, JointType.WristRight, 90, 20);


            //Pose 2 - 把手放下来 Both Hands Down
            this.poseLibrary[1] = new Pose();
            this.poseLibrary[1].Title = "把手放下来（Arms Down）";
            this.poseLibrary[1].Angles = new PoseAngle[4];
            this.poseLibrary[1].Angles[0] = new PoseAngle(JointType.ShoulderLeft, JointType.ElbowLeft, 180, 20);
            this.poseLibrary[1].Angles[1] = new PoseAngle(JointType.ElbowLeft, JointType.WristLeft, 270, 20);
            this.poseLibrary[1].Angles[2] = new PoseAngle(JointType.ShoulderRight, JointType.ElbowRight, 0, 20);
            this.poseLibrary[1].Angles[3] = new PoseAngle(JointType.ElbowRight, JointType.WristRight, 270, 20);


            //Pose 3 - 举起左手 Left Up and Right Down
            this.poseLibrary[2] = new Pose();
            this.poseLibrary[2].Title = "（举起左手）Left Up and Right Down";
            this.poseLibrary[2].Angles = new PoseAngle[4];
            this.poseLibrary[2].Angles[0] = new PoseAngle(JointType.ShoulderLeft, JointType.ElbowLeft, 180, 20);
            this.poseLibrary[2].Angles[1] = new PoseAngle(JointType.ElbowLeft, JointType.WristLeft, 90, 20);
            this.poseLibrary[2].Angles[2] = new PoseAngle(JointType.ShoulderRight, JointType.ElbowRight, 0, 20);
            this.poseLibrary[2].Angles[3] = new PoseAngle(JointType.ElbowRight, JointType.WristRight, 270, 20);


            //Pose 4 - 举起右手 Right Up and Left Down
            this.poseLibrary[3] = new Pose();
            this.poseLibrary[3].Title = "（举起右手）Right Up and Left Down";
            this.poseLibrary[3].Angles = new PoseAngle[4];
            this.poseLibrary[3].Angles[0] = new PoseAngle(JointType.ShoulderLeft, JointType.ElbowLeft, 180, 20);
            this.poseLibrary[3].Angles[1] = new PoseAngle(JointType.ElbowLeft, JointType.WristLeft, 270, 20);
            this.poseLibrary[3].Angles[2] = new PoseAngle(JointType.ShoulderRight, JointType.ElbowRight, 0, 20);
            this.poseLibrary[3].Angles[3] = new PoseAngle(JointType.ElbowRight, JointType.WristRight, 90, 20);
        }

        private bool IsPose(Skeleton skeleton, Pose pose)
        {
            bool isPose = true;
            double angle;
            double poseAngle;
            double poseThreshold;
            double loAngle;
            double hiAngle;

            for (int i = 0; i < pose.Angles.Length && isPose; i++)
            {
                poseAngle = pose.Angles[i].Angle;
                poseThreshold = pose.Angles[i].Threshold;
                angle = GetJointAngle(skeleton.Joints[pose.Angles[i].CenterJoint], skeleton.Joints[pose.Angles[i].AngleJoint]);

                hiAngle = poseAngle + poseThreshold;
                loAngle = poseAngle - poseThreshold;

                if (hiAngle >= 360 || loAngle < 0)
                {
                    loAngle = (loAngle < 0) ? 360 + loAngle : loAngle;
                    hiAngle = hiAngle % 360;

                    isPose = !(loAngle > angle && angle > hiAngle);
                }
                else
                {
                    isPose = (loAngle <= angle && hiAngle >= angle);
                }
            }

            return isPose;
        }

        private double GetJointAngle(Joint centerJoint, Joint angleJoint)
        {
            Point primaryPoint = GetJointPoint(this.sensorChooser.Kinect, centerJoint, this.handCanvas.RenderSize, new Point());
            Point anglePoint = GetJointPoint(this.sensorChooser.Kinect, angleJoint, this.handCanvas.RenderSize, new Point());
            Point x = new Point(primaryPoint.X + anglePoint.X, primaryPoint.Y);

            double a;
            double b;
            double c;

            a = Math.Sqrt(Math.Pow(primaryPoint.X - anglePoint.X, 2) + Math.Pow(primaryPoint.Y - anglePoint.Y, 2));
            b = anglePoint.X;
            c = Math.Sqrt(Math.Pow(anglePoint.X - x.X, 2) + Math.Pow(anglePoint.Y - x.Y, 2));

            double angleRad = Math.Acos((a * a + b * b - c * c) / (2 * a * b));
            double angleDeg = angleRad * 180 / Math.PI;

            if (primaryPoint.Y < anglePoint.Y)
            {
                angleDeg = 360 - angleDeg;
            }

            return angleDeg;
        }

    }
}
