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
//
using Microsoft.Kinect; // 레퍼런스에 Microsoft.Kinect 추가 
using Microsoft.Kinect.Toolkit;
using Microsoft.Kinect.Toolkit.FaceTracking;

namespace KD01
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : Window
    {
        //프레임 분할 수를 정의한다. - 화면분할을 원하는대로 수정 할 수 있음
        public const int partitionWidth = 20;
        public const int partitionHeight = 20;

        //방향판단 분할 수를 정의한다 - 방향분할을 원하는대로 수정 할 수 있음
        public const int directionPartWidth = 3;
        public const int directionPartHeight = 3;

        //키넥트 센서
        KinectSensor nui1 = null;
        KinectSensor nui2 = null;

        //Frame들 정의
        private byte[] colorImage;
        private ColorImageFormat colorImageFormat = ColorImageFormat.Undefined;
        private short[] depthImage;
        private DepthImageFormat depthImageFormat = DepthImageFormat.Undefined;
        private Skeleton[] skeletonData;
        private FaceTracker faceTracker;
        private EnumIndexableCollection<FeaturePoint, PointF> facePoints;

        //시선검사 관련 변수 정의 
        double m_headX = 0;
        double m_headY = 0;
        double m_noseX = 0;
        double m_noseY = 0;
        double m_prevHeadX = 0;
        double m_prevHeadY = 0;

        Line lineOfSight = new Line();
        bool[,] m_sightDirection = new bool[directionPartWidth, directionPartHeight];
        Rectangle m_sightRect = new Rectangle();
        Rectangle[] m_whichSightRect = new Rectangle[directionPartWidth * directionPartHeight];

        //장애물 검사 관련 변수 정의
        long m_saveFrame = 0;
        long m_saveTime = 0;
        int[, ,] m_whichCell = new int[partitionWidth, partitionHeight, 5];
        Rectangle[] m_rectangle = new Rectangle[partitionWidth * partitionHeight];
        bool[, ,] m_objectByDirection = new bool[directionPartWidth, directionPartHeight, 5];
        Rectangle[] m_objectRect = new Rectangle[directionPartWidth * directionPartHeight]; 

        public MainWindow()
        {
            InitializeComponent();
            InitializeNui();

            //캔버스에 그릴 도형들 초기화 
            lineOfSight.Stroke = System.Windows.Media.Brushes.LightSteelBlue;
            lineOfSight.HorizontalAlignment = HorizontalAlignment.Left;
            lineOfSight.VerticalAlignment = VerticalAlignment.Bottom;
            lineOfSight.StrokeThickness = 2;
            canvas1.Children.Add(lineOfSight);

            m_sightRect.Stroke = Brushes.Blue;
            m_sightRect.StrokeThickness = 3;
            m_sightRect.Width = 70;
            m_sightRect.Height = 50;
            canvas1.Children.Add(m_sightRect);

            // 시선 표시 사각형 초기화
            for (int i = 0; i < directionPartWidth * directionPartHeight; i++)
            {
                m_whichSightRect[i] = new Rectangle();

                Canvas.SetLeft(m_whichSightRect[i], 0);
                Canvas.SetRight(m_whichSightRect[i], 0);

                m_whichSightRect[i].Width = 0;
                m_whichSightRect[i].Height = 0;
                m_whichSightRect[i].Stroke = Brushes.Yellow;
                m_whichSightRect[i].StrokeThickness = 8;

                canvas1.Children.Add(m_whichSightRect[i]);
            }

            //시선 방향 배열 초기화        
            for (int i = 0; i < directionPartWidth; i++)
            {
                for (int j = 0; j < directionPartHeight; j++)
                {
                    m_sightDirection[i, j] = false;
                }
            }

            // 프레임 분할 수 만큼 사각형 초기화
            for (int i = 0; i < partitionWidth * partitionHeight; i++)
            {
                m_rectangle[i] = new Rectangle();

                Canvas.SetLeft(m_rectangle[i], 0);
                Canvas.SetRight(m_rectangle[i], 0);

                m_rectangle[i].Width = 30;
                m_rectangle[i].Height = 30;
                m_rectangle[i].Stroke = Brushes.Blue;
                m_rectangle[i].StrokeThickness = 3;

                canvas1.Children.Add(m_rectangle[i]);
            }

            // 방향에 따른 장애물 배열 초기화 
            for (int i = 0; i < directionPartWidth; i++)
            {
                for (int j = 0; j < directionPartHeight; j++)
                {
                    for (int k = 0; k < 5; k++)
                    {
                        m_objectByDirection[i, j, k] = false;
                    }
                }
            }

            // 장애물 표시 사각형 초기화
            for (int i = 0; i < directionPartWidth * directionPartHeight; i++)
            {
                m_objectRect[i] = new Rectangle();

                Canvas.SetLeft(m_objectRect[i], 0);
                Canvas.SetRight(m_objectRect[i], 0);

                m_objectRect[i].Width = 0;
                m_objectRect[i].Height = 0;
                m_objectRect[i].Stroke = Brushes.Green;
                m_objectRect[i].StrokeThickness = 5;

                canvas1.Children.Add(m_objectRect[i]);
            }

        }
       
        void InitializeNui()
        {
            nui1 = KinectSensor.KinectSensors[0];

            nui1.ColorStream.Enable();
            nui1.ColorFrameReady += new EventHandler<ColorImageFrameReadyEventArgs>(nui_ColorFrameReady);

            nui1.DepthStream.Enable(DepthImageFormat.Resolution320x240Fps30);
            nui1.SkeletonStream.Enable();
            nui1.SkeletonStream.TrackingMode = SkeletonTrackingMode.Seated;
            nui1.AllFramesReady += new EventHandler<AllFramesReadyEventArgs>(nui_AllFramesReady);

            nui1.Start();

            nui2 = KinectSensor.KinectSensors[1];

            nui2.DepthStream.Enable(DepthImageFormat.Resolution320x240Fps30);
            nui2.DepthFrameReady += new EventHandler<DepthImageFrameReadyEventArgs>(nui_DepthFrameReady);

            nui2.Start();
        }

        // frame수로 시간을 재는 함수  
        void SetFrameTime(DepthImageFrame depthFrame)
        {
            long lFrame = (long)depthFrame.FrameNumber;
            long lTime = (long)depthFrame.Timestamp;

            if (lTime - m_saveTime > 1000)
            {
                m_saveFrame = lFrame;
                m_saveTime = lTime;


                // 1초마다 방향에 따른 장애물 배열의 0초부분을 초기화
                for (int i = 0; i < directionPartWidth; i++)
                {
                    for (int j = 0; j < directionPartHeight; j++)
                    {
                        bool temp = m_objectByDirection[i, j, 0];
                        for (int k = 1; k < 5; k++)
                        {
                            bool temp2 = m_objectByDirection[i, j, k];
                            m_objectByDirection[i, j, k] = temp;
                            temp = temp2;
                        }
                        m_objectByDirection[i, j, 0] = false;
                        int whichRec = i + j * directionPartWidth;
                        m_objectRect[whichRec].Width = 0;
                        m_objectRect[whichRec].Height = 0;
                    }
                }

                //1초마다 시선의 방향 배열 초기화                
                for (int i = 0; i < directionPartWidth; i++)
                {
                    for (int j = 0; j < directionPartHeight; j++)
                    {
                        m_sightDirection[i, j] = false;
                    }
                }

            }
        }

        void SetRGB(byte[] nPlayers, int nPos, byte r, byte g, byte b)
        {
            nPlayers[nPos + 2] = r;
            nPlayers[nPos + 1] = g;
            nPlayers[nPos + 0] = b;
        }

        // 각 영역의 장애물의 좌표값을 배열에 저장 
        void SetCellObjCoordinate(int cell_x, int cell_y, int x, int y, int distance)
        {
            m_whichCell[cell_x, cell_y, 0] = Math.Min(m_whichCell[cell_x, cell_y, 0], x);
            m_whichCell[cell_x, cell_y, 1] = Math.Min(m_whichCell[cell_x, cell_y, 1], y);
            m_whichCell[cell_x, cell_y, 2] = Math.Max(m_whichCell[cell_x, cell_y, 2], x);
            m_whichCell[cell_x, cell_y, 3] = Math.Max(m_whichCell[cell_x, cell_y, 3], y);
            m_whichCell[cell_x, cell_y, 4] = distance;

        }

        // 저장된 좌표에 따라 각 셀의 사각형 그리기 
        void DrawCellRectangle()
        {
            int whichRec = 0;
            for (int i = 0; i < partitionWidth; i++)
            {
                for (int j = 0; j < partitionHeight; j++)
                {
                    Canvas.SetLeft(m_rectangle[whichRec], m_whichCell[i, j, 0] * 2);
                    Canvas.SetTop(m_rectangle[whichRec], m_whichCell[i, j, 1] * 2);

                    m_rectangle[whichRec].Width = (m_whichCell[i, j, 2] - m_whichCell[i, j, 0]) * 2;
                    m_rectangle[whichRec].Height = (m_whichCell[i, j, 3] - m_whichCell[i, j, 1]) * 2;

                    whichRec++;
                }
            }
        }


        // Object의 방향 판단
        void CheckDirection(DepthImageFrame PImage, int cell_x, int cell_y, int timer)
        {

            int direct_x = cell_x / (partitionWidth / directionPartWidth);
            int direct_y = cell_y / (partitionHeight / directionPartHeight);

            // 화면분할과 방향분할이 딱 떨어지지 않을 경우를 위한 예외처리
            if ((partitionWidth % directionPartWidth) > 0)
            {
                if (direct_x > (directionPartWidth - 1))
                    direct_x--;
            }

            if ((partitionHeight % directionPartHeight) > 0)
            {
                if (direct_y > (directionPartHeight - 1))
                    direct_y--;
            }

            m_objectByDirection[direct_x, direct_y, timer] = true;

            int whichRec = direct_x + direct_y * directionPartWidth;
            if (m_objectByDirection[direct_x, direct_y, 0] && m_objectByDirection[direct_x, direct_y, 1] && m_objectByDirection[direct_x, direct_y, 2] && m_objectByDirection[direct_x, direct_y, 3] && m_objectByDirection[direct_x, direct_y, 4])
            {
                // 장애물이 있는 칸을 알려주기 위한 네모를 그림 
                Canvas.SetLeft(m_objectRect[whichRec], (PImage.Width / directionPartWidth * direct_x) * 2);
                Canvas.SetTop(m_objectRect[whichRec], (PImage.Height / directionPartHeight * direct_y) * 2);

                m_objectRect[whichRec].Width = PImage.Width / directionPartWidth * 2;
                m_objectRect[whichRec].Height = PImage.Height / directionPartHeight * 2;

                //장애물 위치에 시선이 있다면 
                if (m_sightDirection[direct_x, direct_y] == true)
                {
                    textBlock4.Text = string.Format("사용자가 장애물을 제대로 응시하고 있습니다." );
                }
                else
                {
                    textBlock4.Text = string.Format("사용자가 장애물을 응시하고 있지 않습니다." );
                }

            }
            else
            {
                m_objectRect[whichRec].Width = 0;
                m_objectRect[whichRec].Height = 0;
            }
        }

        
        byte[] GetRGB(DepthImageFrame PImage, short[] depthFrame, DepthImageStream depthStream)
        {
            byte[] rgbs = new byte[PImage.Width * PImage.Height * 4];

            int nNear = 999999;
            int nSavei32 = 0;
            int nTimer = (int)(m_saveFrame % 150 / 30);

            // 각 영역의 좌표값 초기화 
            for (int i = 0; i < partitionWidth; i++)
            {
                for (int j = 0; j < partitionHeight; j++)
                {
                    m_whichCell[i, j, 0] = int.MaxValue;
                    m_whichCell[i, j, 1] = int.MaxValue;
                    m_whichCell[i, j, 2] = int.MinValue;
                    m_whichCell[i, j, 3] = int.MinValue;
                    m_whichCell[i, j, 4] = int.MinValue;
                }
            }

            for (int i16 = 0, i32 = 0; i16 < depthFrame.Length && i32 < rgbs.Length; i16++, i32 += 4)
            {
                int nDistance = depthFrame[i16] >> 
                                DepthImageFrame.PlayerIndexBitmaskWidth;

                int nX = i16 % PImage.Width;
                int nY = i16 / PImage.Width;

                int nCellX = (nX - 1) / (PImage.Width / partitionWidth);
                int nCellY = (nY - 1) / (PImage.Height / partitionHeight);

                // 화면이 나눠떨어지지 않는경우 예외처리 
                if (nCellX >= partitionWidth)
                    nCellX--;

                if (nCellY >= partitionHeight)
                    nCellY--;
                
                if (nNear > nDistance && nDistance > 400)
                {
                    nNear = nDistance;
                    nSavei32 = i32;
                }

                int nColor = 0xFF * nDistance / 4000;
                
                // 거리가 0~200 이라면 장애물로 판단, 
                if (nDistance >= 0 && nDistance <= 200)
                {
                    // 해당 물체의 색을 red 로 처리하고 좌표값을 저장하는 함수와 장애물의 방향을 판단하는 함수를 호출한다. 
                    SetRGB(rgbs, i32, 0xff, 0, 0);
                    SetCellObjCoordinate(nCellX, nCellY, nX, nY, nDistance);
                    CheckDirection(PImage, nCellX, nCellY, 0);
                }
                else
                    SetRGB(rgbs, i32, (byte)nColor, (byte)nColor, (byte)nColor);
            }

            SetRGB(rgbs, nSavei32, 0xFF, 0, 0);
            

            return rgbs;
        }

        //시선의 위치를 체크하는 알고리즘 
        void CheckWhichSight(DepthImageFrame depthFrame, double noseX, double noseY)
        {
            double whichSightX = (noseX - (m_headX - m_sightRect.Width / 2)) / (m_sightRect.Width / directionPartWidth);
            double whichSightY = (noseY - m_headY) / (m_sightRect.Height / directionPartHeight);

            textBlock3.Text = string.Format("whichSightX : {0}, whichSightY : {1}", (int)whichSightX, (int)whichSightY);

            // 예외처리
            if (whichSightX > 2)
            {
                whichSightX = 2;
            }
            else if(whichSightX < 0)
            {
                whichSightX = 0;
            }

            if (whichSightY > 2)
            {
                whichSightY = 2;
            }
            else if (whichSightY < 0)
            {
                whichSightY = 0;
            }

            m_sightDirection[(int)whichSightX, (int)whichSightY] = true;

            // 시선이 위치하는곳에 사각형을 그린다. 
            int whichRec = (int)whichSightX + (int)whichSightY * directionPartWidth;
            for (int i = 0; i < directionPartWidth * directionPartHeight; i++)
            {
                if (i == whichRec)
                {
                    Canvas.SetLeft(m_whichSightRect[i], (depthFrame.Width / directionPartWidth * (int)whichSightX) * 2);
                    Canvas.SetTop(m_whichSightRect[i], (depthFrame.Height / directionPartHeight * (int)whichSightY) * 2);

                    m_whichSightRect[i].Width = depthFrame.Width / directionPartWidth * 2;
                    m_whichSightRect[i].Height = depthFrame.Height / directionPartHeight * 2;
                }
                else
                {
                    m_whichSightRect[i].Width = 0;
                    m_whichSightRect[i].Height = 0;
                }
            }
        }

        void nui_DepthFrameReady(object sender, DepthImageFrameReadyEventArgs e)
        {
            DepthImageFrame ImageParam = e.OpenDepthImageFrame();

            if (ImageParam == null) return;

            short[] ImageBits = new short[ImageParam.PixelDataLength];
            ImageParam.CopyPixelDataTo(ImageBits);

            WriteableBitmap wb = new WriteableBitmap(ImageParam.Width,
                                                     ImageParam.Height,
                                                     96, 96,
                                                     PixelFormats.Bgr32, null);
            wb.WritePixels(new Int32Rect(0, 0,
                                            ImageParam.Width,
                                            ImageParam.Height),
                            GetRGB(ImageParam, ImageBits, nui2.DepthStream),
                            ImageParam.Width * 4,
                            0);

            SetFrameTime(ImageParam);
            DrawCellRectangle();

        }

        void nui_ColorFrameReady(object sender, ColorImageFrameReadyEventArgs e)
        {
            ColorImageFrame ImageParam = e.OpenColorImageFrame();

            if (ImageParam == null) return;

            byte[] ImageBits = new byte[ImageParam.PixelDataLength];
            ImageParam.CopyPixelDataTo(ImageBits);

            BitmapSource src = null;
            src = BitmapSource.Create(  ImageParam.Width,  ImageParam.Height,
                                        96, 96,PixelFormats.Bgr32,null,
                                        ImageBits,
                                        ImageParam.Width * ImageParam.BytesPerPixel);
            image1.Source = src;
        }

        void nui_AllFramesReady(object sender, AllFramesReadyEventArgs e)
        {

            ColorImageFrame colorImageFrame = null;
            DepthImageFrame depthImageFrame = null;
            SkeletonFrame skeletonFrame = null;

            try
            {
                colorImageFrame = e.OpenColorImageFrame();
                depthImageFrame = e.OpenDepthImageFrame();
                skeletonFrame = e.OpenSkeletonFrame();

                if (colorImageFrame == null || depthImageFrame == null || skeletonFrame == null)
                {
                    return;
                }

                if (this.depthImageFormat != depthImageFrame.Format)
                {
                    this.depthImage = null;
                    this.depthImageFormat = depthImageFrame.Format;
                }

                if (this.colorImageFormat != colorImageFrame.Format)
                {
                    this.colorImage = null;
                    this.colorImageFormat = colorImageFrame.Format;
                }

                if (this.depthImage == null)
                {
                    this.depthImage = new short[depthImageFrame.PixelDataLength];
                }

                if (this.colorImage == null)
                {
                    this.colorImage = new byte[colorImageFrame.PixelDataLength];
                }

                if (this.skeletonData == null || this.skeletonData.Length != skeletonFrame.SkeletonArrayLength)
                {
                    this.skeletonData = new Skeleton[skeletonFrame.SkeletonArrayLength];
                }

                colorImageFrame.CopyPixelDataTo(this.colorImage);
                depthImageFrame.CopyPixelDataTo(this.depthImage);
                skeletonFrame.CopySkeletonDataTo(this.skeletonData);
            }
            finally
            {
                if (colorImageFrame != null)
                {
                    colorImageFrame.Dispose();
                }

                if (depthImageFrame != null)
                {
                    depthImageFrame.Dispose();
                }

                if (skeletonFrame != null)
                {
                    skeletonFrame.Dispose();
                }
            }


            using (depthImageFrame)
            {
                if (depthImageFrame != null)
                {
                    foreach (Skeleton skeleton in skeletonData)
                    {
                        if (skeleton.TrackingState == SkeletonTrackingState.Tracked || skeleton.TrackingState == SkeletonTrackingState.PositionOnly)
                        {
                            /////////// 머리 정보를 받아온다 ///////////////
                            Joint joint = skeleton.Joints[JointType.Head];

							DepthImagePoint depthPoint;
							depthPoint = depthImageFrame.MapFromSkeletonPoint(joint.Position);
                            
                            System.Windows.Point point = new System.Windows.Point((int)(image1.ActualWidth  * depthPoint.X 
                                                               / depthImageFrame.Width),
													(int)(image1.ActualHeight * depthPoint.Y 
                                                               / depthImageFrame.Height));

                            textBlock1.Text = string.Format("X:{0:0.00} Y:{1:0.00} Z:{2:0.00}", point.X, point.Y, joint.Position.Z);

                            // 이전 헤드의 위치를 저장한다.
                            m_prevHeadX = m_headX;
                            m_prevHeadY = m_headY;
                            m_headX = point.X;
                            m_headY = point.Y;

                            if (Math.Abs(m_prevHeadX - point.X) < 10 )
                            {
                                m_headX = m_prevHeadX;
                            }

                            if (Math.Abs(m_prevHeadY - point.Y) < 10)
                            {
                                m_headY = m_prevHeadY;
                            }

                            Canvas.SetLeft(ellipse1, point.X - ellipse1.Width / 2);
                            Canvas.SetTop(ellipse1, point.Y - ellipse1.Height / 2);

                            ////////////// face 정보를 받아온다//////////////////////
                            if (this.faceTracker == null)
                            {
                                try
                                {
                                    this.faceTracker = new FaceTracker(nui1);
                                }
                                catch (InvalidOperationException)
                                {
                                    // During some shutdown scenarios the FaceTracker
                                    // is unable to be instantiated.  Catch that exception
                                    // and don't track a face.
                                    this.faceTracker = null;
                                }
                            }

                            if (this.faceTracker != null)
                            {
                                FaceTrackFrame frame = this.faceTracker.Track(
                                    colorImageFormat, colorImage, depthImageFormat, depthImage, skeleton);

                                if (frame.TrackSuccessful)
                                {
                                    facePoints = frame.GetProjected3DShape();

                                    textBlock2.Text = string.Format("noseX:{0:0.00} noseY:{1:0.00} ", facePoints[107].X, facePoints[107].Y);

                                    m_noseX = facePoints[107].X;
                                    m_noseY = facePoints[107].Y;

                                    Canvas.SetLeft(ellipse2, facePoints[107].X - ellipse2.Width / 2);
                                    Canvas.SetTop(ellipse2, facePoints[107].Y - ellipse2.Width / 2);

                                }
                            }

                            ///////////////고개의 각도를 계산 ////////////////////

                            lineOfSight.X1 = m_headX;
                            lineOfSight.Y1 = m_headY;
                            lineOfSight.X2 = m_noseX;
                            lineOfSight.Y2 = m_noseY;

                            Canvas.SetLeft(m_sightRect, m_headX - m_sightRect.Width / 2);
                            Canvas.SetTop(m_sightRect, m_headY);

                            CheckWhichSight(depthImageFrame, m_noseX, m_noseY);

                        }
                    }
                }
            }
        }
    }
}
