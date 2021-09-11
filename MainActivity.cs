// --------------------------------------------------------------------------------------------------------------------
// <copyright file="MainActivity.cs" company="Utrecht University">
//   This program has been developed by students from the bachelor Computer Science at Utrecht University within the
//   Software and Game project course winter 2018-2019.
//   ©Copyright Utrecht University(Department of Information and Computing Sciences)
// </copyright>
// <summary> Visual Epilepsy Android main activity. This class handles all I/O and logic. </summary>
// --------------------------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("VisualEpilepsy-Android.Tests")]

namespace VisualEpilepsy_Android
{
    using System;
    using System.Threading;

    using Android;
    using Android.App;
    using Android.Content;
    using Android.Content.PM;
    using Android.Content.Res;
    using Android.Gms.Vision;
    using Android.Graphics;
    using Android.OS;
    using Android.Support.V7.App;
    using Android.Util;
    using Android.Views;
    using Android.Widget;

    using AlertDialog = Android.App.AlertDialog;
    using Orientation = Android.Content.Res.Orientation;

    using Runnable = Java.Lang.Runnable;

    /// <summary> The Visual Epilepsy Android main activity.
    /// This class handles all I/O and logic. </summary>
    [Activity(
        Label = "@string/app_name",
        Icon = "@drawable/icon",
        Theme = "@style/AppTheme.NoActionBar",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.ScreenSize)]
    public class MainActivity : AppCompatActivity, ISurfaceHolderCallback, Detector.IProcessor
    {
        #region Variables

        /// <summary> ID to ask the user for camera permission. </summary>
        private const int RequestCameraPermissionId = 1001;

        /// <summary> The camera source. </summary>
        private CameraSource cameraSource;

        /// <summary> The start time for the <see cref="timer"/>. </summary>
        private DateTime startTime;

        /// <summary> The time when the last image was saved. </summary>
        private DateTime saveImageStartTime;

        /// <summary> Variable indicating whether the application is recording or not. </summary>
        private bool recording;

        /// <summary> The Timer used for timing the duration of a session analytics session. </summary>
        private Timer timer;

        /// <summary> The TextView used to display the duration of a session analytics session. </summary>
        private TextView timerTextView;

        /// <summary> Detector used to put a frame through the pipeline. </summary>
        private PipelineDetector pipelineDetector;

        /// <summary> The surface on which we project the <see cref="cameraSource"/> stream. </summary>
        private SurfaceView cameraView;

        /// <summary> The surface on which we project the UI. </summary>
        private ImageView imageView;

        /// <summary> The warning drawer, used to draw the grid and its values. </summary>
        private WarningDrawer warningDrawer;

        /// <summary> Button used to go to the settings screen. </summary>
        private Button settingsButton;

        /// <summary> The record button. </summary>
        private ToggleButton recordButton;

        /// <summary> The settings manager, creating access to all the options. </summary>
        private SettingsManager settingsManager;

        /// <summary> Camera preview height. </summary>
        private int cameraPreviewHeight;

        /// <summary> Camera preview width. </summary>
        private int cameraPreviewWidth;

        #endregion

        /// <summary> Displays an error message for the user and shuts the application down. </summary>
        public void ErrorShutdown()
        {
            // Prepare a Looper for the pop-up.
            if (Looper.MyLooper() == null)
            {
                Looper.Prepare();
            }

            // Create a builder to create the error warning dialogue.
            var builder = new AlertDialog.Builder(this);
            builder.SetTitle("Attention!").SetMessage("Something went wrong. The application will now close down.")
                .SetPositiveButton("Close application", (senderAlert, args) => { this.Finish(); });

            // Create a pop-up to show to the user before shutting down.
            // The application will loop over this pop-up in order to wait for the user's input.
            builder.Create().Show();
            Looper.Loop();
        }

        #region ISurfaceHolder implementations

        /// <inheritdoc />
        public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
        {
            // This is a required interface implementation.
            // We do not need this, hence this method is empty.
        }

        /// <inheritdoc />
        public void SurfaceCreated(ISurfaceHolder holder)
        {
            // Request permission from the user to access the Camera. 
            // If permission has already been granted, the user won't be prompted.
            this.RequestPermissions(new[] { Manifest.Permission.Camera }, RequestCameraPermissionId);
        }

        /// <inheritdoc />
        public void SurfaceDestroyed(ISurfaceHolder holder)
        {
            // Stop the cameraSource.
            this.cameraSource.Dispose();
        }

        #endregion

        #region IProcessor Implementations

        /// <summary> All <paramref name="detections"/> are written as a string,
        /// ready to be visualized on the canvas used in warningDrawer. </summary>
        /// <param name="detections"> The detections which are found on a frame
        /// from the <see cref="cameraSource"/>. </param>
        public void ReceiveDetections(Detector.Detections detections)
        {
            // Check if there is only 1 detection. This could mean something went wrong.
            if (detections.DetectedItems.Size() == 1)
            {
                // Check if the result is negative, if it is, something has either gone wrong in the pipeline,
                // or it has thrown an exception.
                if (float.Parse(detections.DetectedItems.ValueAt(0).ToString()) < 0)
                {
                    // The pipeline returned a negative output.
                    this.ErrorShutdown();
                }
            }

            // Check if the drawer is done drawing the canvas, a check is needed
            // otherwise an unfinished canvas will be added.
            var gridFilled = false;

            // Extract SparseArray from detections and fill grid with its output values.
            this.RunOnUiThread(
                () =>
                    {
                        this.warningDrawer.FillGrid(detections.DetectedItems);
                        gridFilled = true;
                    });

            // A communication problem arises between the main thread and the UI thread
            // when the rest of the method is handled in the UI thread.
            // Thus the choice is made to wait for a response from the UI thread using the boolean gridFilled,
            // because this is quick enough.

            // If the current frame does not need to be exported,
            // analytics are not enabled,
            // the application is not recording, or
            // the previously saved image is within the given minimum amount of ms,
            // don't save the frame and just continue.
            if (!Analytics.ExportCurrentFrame || !this.settingsManager.Analytics || !this.recording
                || (DateTime.Now - this.saveImageStartTime).TotalMilliseconds < (int)this.settingsManager.CurrentDelayTimer)
            {
                return;
            }

            // Store the moment when the new frame is saved.
            this.saveImageStartTime = DateTime.Now;

            // Wait for the values to be drawn on the ImageView before exporting the frame.
            SpinWait.SpinUntil(() => gridFilled);

            this.ExportFrame();
        }

        /// <inheritdoc />
        public void Release()
        {
            // This is a required detector implementation.
            // We do not need this, hence this method is empty.
        }

        #endregion

        /// <summary> Export the previously processed Frame with the pipeline values drawn on top of it. </summary>
        public void ExportFrame()
        {
            // The Bitmap of the drawn (warning) grid needs to be scaled to the dimensions of the frame bitmap,
            // because the dimensions of the frame can differ from the surface view.
            Bitmap scaledSessionData;

            // When in portrait mode, the height and width are swapped.
            // Scale the Bitmap, with the values drawn on it, to the correct size.
            if (this.WindowManager.DefaultDisplay.Rotation == SurfaceOrientation.Rotation0)
            {
                scaledSessionData = Bitmap.CreateScaledBitmap(
                    this.warningDrawer.Bitmap,
                    this.cameraSource.PreviewSize.Height,
                    this.cameraSource.PreviewSize.Width,
                    false);
            }
            else
            {
                scaledSessionData = Bitmap.CreateScaledBitmap(
                    this.warningDrawer.Bitmap,
                    this.cameraSource.PreviewSize.Width,
                    this.cameraSource.PreviewSize.Height,
                    false);
            }

            // Combine the Bitmap containing the analytics data with the Bitmap of the analysed camera frame.
            var exportImage = Analytics.CombineBitmaps(Analytics.CameraBitmap, scaledSessionData);

            // Add the Bitmap to the end of the export queue.
            Analytics.ExportQueue.Enqueue(exportImage);

            // Save the combined Bitmap.
            Analytics.SaveImage();
        }

        /// <summary> Checks whether the user has granted the needed <paramref name="permissions"/> or not. </summary>
        /// <param name="requestCode"> The request code of the permission. </param>
        /// <param name="permissions"> All permissions that have been asked of the user. </param>
        /// <param name="grantResults"> The results denoting whether the user has allowed the needed permissions. </param>
        public override void OnRequestPermissionsResult(
            int requestCode,
            string[] permissions,
            Permission[] grantResults)
        {
            // If permissions is null, throw an exception.
            if (permissions == null)
            {
                this.ExceptionToast();
                throw new ArgumentNullException(nameof(permissions));
            }

            // If grantResults is null, throw an exception.
            if (grantResults == null)
            {
                this.ExceptionToast();
                throw new ArgumentNullException(nameof(grantResults));
            }

            // Switch on the request code.
            // This is done in a switch statement to guarantee extensibility.
            // Making this an if-statement should be done with care as this function is called by Android.
            switch (requestCode)
            {
                // In case the request was for the camera.
                case RequestCameraPermissionId:
                    // If permission to use the camera was granted.
                    if (grantResults[0] == Permission.Granted)
                    {
                        // Start the cameraSource.
                        this.cameraSource.Start(this.cameraView.Holder);

                        // Get the general information about the display.
                        var displayMetrics = new DisplayMetrics();
                        this.WindowManager.DefaultDisplay.GetRealMetrics(displayMetrics);

                        // Update the layout according to the orientation and aspect ratio.
                        this.UpdateCameraViewAspectRatio();
                    }
                    else
                    {
                        // Create a builder to create a warning dialogue.
                        var alert = new AlertDialog.Builder(this);
                        alert.SetTitle("Attention!")
                            .SetMessage("The application cannot function without access to the camera.")
                            .SetPositiveButton("Close application", (senderAlert, args) => { this.Finish(); });

                        // Create a warning dialogue and show to user.
                        alert.Create().Show();
                    }

                    break;
                default:
                    this.ExceptionToast();
                    throw new ArgumentException($"The request code is incorrect: {requestCode}");
            }
        }

        /// <inheritdoc/>
        public override void OnConfigurationChanged(Configuration newConfig)
        {
            // Call the base event.
            base.OnConfigurationChanged(newConfig);

            // Update the aspect ratio.
            this.UpdateCameraViewAspectRatio();
        }

        /// <summary> The on save instance state. </summary>
        /// <param name="savedInstanceState"> The saved instance state. </param>
        protected override void OnSaveInstanceState(Bundle savedInstanceState)
        {
            base.OnSaveInstanceState(savedInstanceState);

            // When the screen is rotated, changing its orientation, the start time will be saved.
            savedInstanceState.PutString(Helper.TimerVariableName, this.startTime.ToString());
        }

        /// <summary> The on restore instance state. </summary>
        /// <param name="savedInstanceState"> The saved instance state. </param>
        protected override void OnRestoreInstanceState(Bundle savedInstanceState)
        {
            base.OnRestoreInstanceState(savedInstanceState);

            // After the screen is rotated the start time will be restored.
            // This way the timer won't reset when the screen is rotated.
            this.startTime = Convert.ToDateTime(savedInstanceState.GetString(Helper.TimerVariableName));
        }

        /// <inheritdoc />
        protected override void OnRestart()
        {
            // Restart the application as it normally does.
            base.OnRestart();

            // Build the CameraSource because we Disposed it.
            this.BuildCamera();

            // In addition to the restart, update the camera aspect ratio.
            this.UpdateCameraViewAspectRatio();
        }

        /// <summary> Runs when the activity is created. </summary>
        /// <param name="savedInstanceState"> The saved instance of this activity. </param>
        protected override void OnCreate(Bundle savedInstanceState)
        {
            // The savedInstanceState can be null, the first time it gets called.
            base.OnCreate(savedInstanceState);

            // Import the main layout.
            this.SetContentView(Resource.Layout.activity_main);

            // Link the cameraView to the SurfaceView in the layout.
            this.cameraView = this.FindViewById<SurfaceView>(Resource.Id.surface_view);

            // Link the imageView to the ImageView in the layout.
            this.imageView = this.FindViewById<ImageView>(Resource.Id.warning_view);

            // Initialise the button functionality.
            this.InitialiseButtonFunctionality();

            // Request the screen to stay on when camera footage is shown.
            this.cameraView.Holder.SetKeepScreenOn(true);

            // Create the pipeline detector.
            this.pipelineDetector = new PipelineDetector();

            // Build the CameraSource.
            this.BuildCamera();

            // Add callback for the detector.
            this.pipelineDetector.SetProcessor(this);

            // Add callback to the activity main service.
            this.cameraView.Holder.AddCallback(this);

            // When the application is rotated, update the aspect ratio.
            this.cameraView.LayoutChange += (o, e) =>
                {
                    // Check if the camera has been built.
                    if (this.cameraSource.PreviewSize != null)
                    {
                        this.UpdateCameraViewAspectRatio();
                    }
                };

            // Link views to the layout.
            this.timerTextView = this.FindViewById<TextView>(Resource.Id.timerTextView);

            // Give the timer a black outline.
            this.timerTextView.SetShadowLayer(1, 0, 0, Color.Black);

            // Initially hide the timer TextView.
            this.timerTextView.Visibility = ViewStates.Invisible;

            // Initially set the start time for when the last image is saved.
            this.saveImageStartTime = DateTime.Now;

            // When the application is started, it is not recording.
            this.recording = false;

            // Initialize the settings manager.
            this.settingsManager = new SettingsManager();
        }

        /// <summary> Creates a bigger 'hitbox' to a given <paramref name="view"/>. </summary>
        /// <param name="view"> The view that needs extra touch space. </param>
        /// <param name="extraSpace"> The amount of extra space (thickness in pixels). </param>
        protected void AddTouchSpace(View view, int extraSpace)
        {
            var parent = view.Parent as View;

            // First check if the view has a parent, then set the values.
            parent?.Post(
                new Runnable(
                    delegate
                        {
                            var touchableArea = new Rect();
                            view.GetHitRect(touchableArea);
                            touchableArea.Top -= extraSpace;
                            touchableArea.Bottom += extraSpace;
                            touchableArea.Left -= extraSpace;
                            touchableArea.Right += extraSpace;
                            parent.TouchDelegate = new TouchDelegate(touchableArea, view);
                        }));
        }

        /// <summary> Initialises the OnClick methods for all buttons in the activity. </summary>
        protected void InitialiseButtonFunctionality()
        {
            // Connect settings button to the xml button.
            this.settingsButton = this.FindViewById<Button>(Resource.Id.button_settings);
            this.AddTouchSpace(this.settingsButton, 20);

            // Add an on click event to the settings button that starts a new SettingsActivity.
            this.settingsButton.Click += (s, e) =>
                {
                    this.settingsButton.Enabled = false;

                    var settingsActivity = new Intent(this, typeof(SettingsActivity));
                    this.StartActivity(settingsActivity);
                };

            // Connect record button to xml button.
            this.recordButton = this.FindViewById<ToggleButton>(Resource.Id.recordButton);

            // Add an on click event to the record button that starts recording.
            this.recordButton.CheckedChange += (o, e) =>
                {
                    if (e.IsChecked)
                    {
                        // Hide the timer and the settings button.
                        this.ToggleVisibility(this.timerTextView);
                        this.ToggleVisibility(this.settingsButton);

                        // Initialise a timer and its start time.
                        this.InitialiseTimer();

                        this.recording = true;
                    }
                    else
                    {
                        // Show the timer and the settings button.
                        this.ToggleVisibility(this.timerTextView);
                        this.ToggleVisibility(this.settingsButton);
                        this.startTime = DateTime.MinValue;

                        // Stop the timer and reset the associated TextView.
                        this.timer.Dispose();
                        this.timerTextView.Text = "00:00";

                        this.recording = false;
                    }
                };
        }

        /// <inheritdoc />
        protected override void OnResume()
        {
            // Checks if the analytics toggle in the option menu is turned on, if so the record button is visible.
            this.recordButton.Visibility = this.settingsManager.Analytics ? ViewStates.Visible : ViewStates.Invisible;
            this.settingsButton.Enabled = true;
            base.OnResume();
        }

        /// <summary> Initialise/reset <see cref="timer"/> and its start time. </summary>
        private void InitialiseTimer()
        {
            // If the start time is not set yet (non set start time is MinValue), set it.
            if (this.startTime == DateTime.MinValue)
            {
                this.startTime = DateTime.Now;
            }

            // If start time is set, timer is running, so update time.
            this.timer = new Timer(this.UpdateTimer, new AutoResetEvent(false), 0, 1000);
        }

        /// <summary> Updates the <see cref="timer"/>. </summary>
        /// <param name="stateInfo"> The state info. </param>
        private void UpdateTimer(object stateInfo)
        {
            this.RunOnUiThread(
                () => { this.timerTextView.Text = (DateTime.Now - this.startTime).ToString(@"mm\:ss"); });
        }

        /// <summary> Toggle the Visibility of a View. </summary>
        /// <param name="view"> The View of which the visibility is being toggled. </param>
        private void ToggleVisibility(View view)
        {
            view.Visibility = (view.Visibility == ViewStates.Visible) ? ViewStates.Invisible : ViewStates.Visible;
        }

        /// <summary> Notify the user that something went wrong. </summary>
        private void ExceptionToast()
        {
            Toast.MakeText(this, "The application closed unexpectedly", ToastLength.Long).Show();
        }

        /// <summary> Updates the cameraView's aspect ratio. </summary>
        private void UpdateCameraViewAspectRatio()
        {
            try
            {
                if (this.cameraSource?.PreviewSize != null)
                {
                    // Save the aspect ratio so we can access this at all times, because it is not always available.
                    this.cameraPreviewHeight = this.cameraSource.PreviewSize.Height;
                    this.cameraPreviewWidth = this.cameraSource.PreviewSize.Width;
                }
                else if (this.cameraPreviewHeight == 0 || this.cameraPreviewWidth == 0)
                {
                    // No width/height is known about the camera and the cameraSource was not initialised properly.
                    this.ExceptionToast();
                    throw new InvalidOperationException("The cameraSource was not initialised properly.");
                }
            }
            catch (Exception e)
            {
                // Check if the cameraSource handle is invalid, if so, we can safely assume the cameraSource was
                // disposed, we need to create a new one and then retry the update.
                // If not, we would create a deadlock so just return instead of the recursion.
                if (typeof(ArgumentException) != e.GetType() || !e.Message.ToLower().Contains("handle must be valid"))
                {
                    return;
                }

                // Build the camera object.
                this.BuildCamera();

                // Retry, because this can only happen when the cameraSource object was disposed,
                // a deadlock will not occur.
                this.UpdateCameraViewAspectRatio();

                return;
            }

            // Get the general information about the display.
            var displayMetrics = new DisplayMetrics();
            this.WindowManager.DefaultDisplay.GetRealMetrics(displayMetrics);

            // Change the resolution of the cameraView the preview is shown on. TODO: maybe change into rotation0
            if (this.Resources.Configuration.Orientation == Orientation.Portrait)
            {
                // If we are in portrait mode.

                // Calculate the ratio it takes to scale up the image to the maximum screen size,
                // this is done for both the width and the height.
                // The image width and height is in landscape mode, while the phone screen width and height
                // are in portrait mode. This is why it is "mixed up".
                var ratioWidth = (float)displayMetrics.WidthPixels / this.cameraPreviewHeight;
                var ratioHeight = (float)displayMetrics.HeightPixels / this.cameraPreviewWidth;

                // Pick the lowest ratio to scale the image width.
                var ratio = Math.Min(ratioHeight, ratioWidth);

                // Set the layout parameters accordingly.
                this.cameraView.LayoutParameters.Height = (int)(this.cameraPreviewWidth * ratio);
                this.cameraView.LayoutParameters.Width = (int)(this.cameraPreviewHeight * ratio);

                // Set up the view in which the output grid and its values will be drawn.
                this.imageView.LayoutParameters.Height = (int)(this.cameraPreviewWidth * ratio);
                this.imageView.LayoutParameters.Width = (int)(this.cameraPreviewHeight * ratio);

                // Create an instance of the warning drawer in which the grid and its output will be displayed.
                this.warningDrawer = new WarningDrawer(
                    this.imageView,
                    this.imageView.LayoutParameters.Width,
                    this.imageView.LayoutParameters.Height,
                    this.WindowManager.DefaultDisplay.Rotation);
            }
            else
            {
                // If we are in landscape mode.

                // Calculate the ratio it takes to scale up the image to the maximum screen size,
                // this is done for both the width and the height.
                var ratioWidth = (float)displayMetrics.WidthPixels / this.cameraPreviewWidth;
                var ratioHeight = (float)displayMetrics.HeightPixels / this.cameraPreviewHeight;

                // Pick the lowest ratio to scale the image width.
                var ratio = Math.Min(ratioHeight, ratioWidth);

                // Set the layout parameters accordingly.
                this.cameraView.LayoutParameters.Height = (int)(this.cameraPreviewHeight * ratio);
                this.cameraView.LayoutParameters.Width = (int)(this.cameraPreviewWidth * ratio);

                // Set up the view in which the output grid and its values will be drawn.
                this.imageView.LayoutParameters.Height = (int)(this.cameraPreviewHeight * ratio);
                this.imageView.LayoutParameters.Width = (int)(this.cameraPreviewWidth * ratio);

                // Create an instance of the warning drawer in which the grid and its output will be displayed.
                this.warningDrawer = new WarningDrawer(
                    this.imageView,
                    this.imageView.LayoutParameters.Width,
                    this.imageView.LayoutParameters.Height,
                    this.WindowManager.DefaultDisplay.Rotation);
            }

            // Pass the application's rotation to analytics.
            Analytics.Rotation = this.WindowManager.DefaultDisplay.Rotation;

            // Build a new CameraSource. This ensures the correct camera orientation.
            this.BuildCamera();

            // Start the Camera Source.
            this.cameraSource.Start(this.cameraView.Holder);

            // Request an update for the new layout on the SurfaceView.
            this.cameraView.RequestLayout();
        }

        /// <summary> Builds the <see cref="cameraSource"/> object. </summary>
        private void BuildCamera()
        {
            // Build the CameraSource.
            this.cameraSource = new CameraSource.Builder(this.ApplicationContext, this.pipelineDetector)
                .SetFacing(CameraFacing.Back).SetAutoFocusEnabled(true).SetRequestedFps(30).Build();
        }
    }
}