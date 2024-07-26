
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

#if NOCHAT_ACTIVE
using Input = NochatScript.Core.NochatInput;
#endif

namespace SaccFlightAndVehicles
{
    [DefaultExecutionOrder(1400)]//before wheels
    [UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
    public class SaccGroundVehicle : UdonSharpBehaviour
    {
        public SaccEntity EntityControl;
        [Tooltip("The object containing all non-trigger colliders for the vehicle, their layers are changed when entering and exiting")]
        public Transform VehicleMesh;
        [Tooltip("Largest renderer on the vehicle, for optimization purposes, checking if visible")]
        public Renderer MainObjectRenderer;
        [Tooltip("Change all children of VehicleMesh, or just the objects with colliders?")]
        public bool OnlyChangeColliders = false;
        [Tooltip("Layer to set the VehicleMesh and it's children to when entering vehicle")]
        public int OnboardVehicleLayer = 31;
        [UdonSynced] public float Health = 73f;
        public Animator VehicleAnimator;
        [System.NonSerialized] public Transform VehicleTransform;
        [System.NonSerialized] public Rigidbody VehicleRigidbody;
        [Tooltip("Number of steps per second engine+wheel physics should run, if refresh rate is higher than this number, it will do nothing. Higher number = more fair physics over different refreshrates at cost of performance.")]
        public int NumStepsSec = 300;
        [Tooltip("List of wheels to send Engine values to and from")]
        public UdonSharpBehaviour[] DriveWheels;
        [Tooltip("Wheels to get the 'Grounded' value from for autosteering")]
        public UdonSharpBehaviour[] SteerWheels;
        [Tooltip("All of the rest of the wheels")]
        public UdonSharpBehaviour[] OtherWheels;
        //public Transform[] DriveWheelsTrans;
        //public sustest[] SteeringWheels;
        //public Transform[] SteeringWheelsTrans;
        [Tooltip("How many revs are added when accelerating")]
        public float DriveSpeed;
        [Tooltip("How many revs are taken away all the time")]
        public float EngineSlowDown = .75f;
        [Tooltip("Throttle that is applied when not touching the controls")]
        public float MinThrottle = .08f;
        [Tooltip("How agressively to reach minthrottle value when not touching the controls")]
        public float MinThrottle_PStrength = 2f;
        [Tooltip("Amount of max DriveSpeed that keyboard users have access to, to stop them spinning out")]
        public float DriveSpeedKeyboardMax = 1f;
        //public float SteerAngle;
        //public float CurrentSteerAngle;
        /*  [UdonSynced(UdonSyncMode.None)]  */

        [Tooltip("How far down you have to push the grip button to grab the joystick and throttle")]
        public float GripSensitivity = .75f;
        [Tooltip("How many degrees the wheel can turn away from neutral position (lock to lock / 2), animation should match this")]
        public float SteeringWheelDegrees = 450f;
        // [Tooltip("How much VR users must twist their hands to reach max throttle, animation should match this")]
        // public float ThrottleDegrees = 50f;
        [Tooltip("How long keyboard turning must be held down to reach full deflection")]
        public float SteeringKeyboardSecsToMax = 0.22222222f;

        [Tooltip("Reduce desktop max steering linearly up to this speed M/s")]
        public float SteeringMaxSpeedDT = 40f;
        [Tooltip("Disable the above feature")]
        public bool SteeringMaxSpeedDTDisabled = false;
        [Tooltip("Steering is reduced but to a minimum of this value")]
        public float DesktopMinSteering = .2f;
        [Tooltip("how fast steering wheel returns to neutral position in destop mode 1 = 1 second, .2 = 5 seconds")]
        public float ThrottleReturnTimeDT = .0001f;
        // [Tooltip("how fast steering wheel returns to neutral position in VR 1 = 1 second, .2 = 5 seconds")]
        // public float ThrottleReturnTimeVR = .1f;
        public float Drag = .02f;
        [Tooltip("Transform to base the pilot's throttle and joystick controls from. Used to make vertical throttle for helicopters, or if the cockpit of your vehicle can move, on transforming vehicle")]
        public Transform ControlsRoot;
        [Tooltip("Efficiency of the engine at different revs, 0=0, 1=revlimiter")]
        public AnimationCurve EngineResponseCurve = AnimationCurve.Linear(0, 1, 1, 1);
        [System.NonSerialized] public Vector3 CurrentVel;
        // [System.NonSerializedAttribute] public bool ThrottleGripLastFrame = false;
        [UdonSynced] public float Fuel = 900;
        [Tooltip("Fuel consumption per second at max revs")]
        public float FuelConsumption = 2;
        /*     [Tooltip("Amount of fuel at which throttle will start reducing")]
            [System.NonSerializedAttribute] public float LowFuel = 125; */
        [Tooltip("Use the left hand trigger to control throttle?")]
        public bool SwitchHandsJoyThrottle = false;
        [Tooltip("Use the left hand grip to grab the steering wheel??")]
        public bool SteeringHand_Left = true;
        [Tooltip("Use the right hand grip to grab the steering wheel??")]
        public bool SteeringHand_Right = true;
        [System.NonSerializedAttribute] public bool _HandBrakeOn;
        [System.NonSerializedAttribute, FieldChangeCallback(nameof(HandBrakeOn_))] public int HandBrakeOn = 0;
        public int HandBrakeOn_
        {
            set
            {
                _HandBrakeOn = value > 0;
                HandBrakeOn = value;
            }
            get => HandBrakeOn;
        }
        private Vector3 VehiclePosLastFrame;
        public bool Bike_AutoSteer;
        public float Bike_AutoSteer_CounterStrength = .01f;
        public float Bike_AutoSteer_Strength = .01f;
        public bool Drift_AutoSteer;
        [Header("AutoSteer (Drift Mode)")]
        [Tooltip("Put in the max degrees the wheels can turn to in order to make autosteer work properly")]
        public float SteeringDegrees = 60;
        public float AutoSteerStrength = 5f;
        [Header("AutoSteer Disabled")]
        [Tooltip("how fast steering wheel returns to neutral position in destop mode 1 = 1 second, .2 = 5 seconds")]
        public float SteeringReturnSpeedDT = .3f;
        [Tooltip("how fast steering wheel returns to neutral position in VR 1 = 1 second, .2 = 5 seconds")]
        public float SteeringReturnSpeedVR = 5f;
        public bool UseStickSteering;
        [Header("Engine")]
        public float RevLimiter = 8000;
        [Tooltip("Vehicle will take damage if experiences more Gs that this (Internally Gs are calculated in all directions, the HUD shows only vertical Gs so it will differ slightly")]
        public float MaxGs = 10;
        [Tooltip("Damage taken Per G above maxGs, per second.\n(Gs - MaxGs) * GDamage = damage/second")]
        public float GDamage = 10f;
        [Header("Other")]
        [Tooltip("Time until vehicle reappears after exploding")]
        public float RespawnDelay = 10;
        [Tooltip("Time after reappearing the vehicle is invincible for")]
        public float InvincibleAfterSpawn = 2.5f;
        [Tooltip("Damage taken when hit by a bullet")]
        public float BulletDamageTaken = 10f;
        [Tooltip("Impact speed that defines a small crash")]
        public float SmallCrashSpeed = 1f;
        [Tooltip("Impact speed that defines a medium crash")]
        public float MediumCrashSpeed = 8f;
        [Tooltip("Impact speed that defines a big crash")]
        public float BigCrashSpeed = 25f;
        public bool PredictDamage = true;
        [Tooltip("Time in seconds it takes to repair fully from 0")]
        public float RepairTime = 30f;
        [Tooltip("Time in seconds it takes to refuel fully from 0")]
        public float RefuelTime = 25f;
        [Tooltip("Range at which vehicle becomes 'distant' for optimization")]
        public float DistantRange = 400f;
        public float RevLimiterDelay = .04f;
        public bool RepeatingWorld = true;
        [Tooltip("Distance you can travel away from world origin before being teleported to the other side of the map. Not recommended to increase, floating point innacuracy and game freezing issues may occur if larger than default")]
        public float RepeatingWorldDistance = 20000;
        [Header("Bike Stuff (WIP/Broken)")]
        [Tooltip("Max roll angle of head for leaning on bike")]
        public float LeanSensitivity_Roll = 25f;
        [Tooltip("How far head has to move to lean forward/back, high number = less movement required")]
        public float LeanSensitivity_Pitch = 2.5f;
        public bool EnableLeaning = false;
        [Tooltip("Completely change how the vehicle operates to behave like a tank, enables two throttle sliders, and turns DriveWheels/SteerWheels into Left/Right tracks")]
        public bool TankMode;
        [Tooltip("Multiply how much the VR throttle moves from hand movement, TankMode only")]
        [System.NonSerializedAttribute] public float ThrottleSensitivity = 6f;
        [Header("Debug")]
        [UdonSynced(UdonSyncMode.Linear)] public float Revs;
        public float Clutch;
        [System.NonSerialized] public int OutsideVehicleLayer;
        public int CurrentGear = 0;
        private bool LimitingRev = false;
        public Vector3 VehicleVel;
        public float debugSpeedSteeringMulti = 0f;
        public bool InVR;
        public bool Sleeping = false;
        public bool Grounded_Steering;
        public bool Grounded;
        public float GearRatio = 0f;
        private float GDamageToTake;
        private float HandDistanceZLastFrame;
        private float VRThrottlePos;
        //twist throttle values
        // private float TempThrottle;
        // private float ThrottleValue;
        // private float ThrottleValueLastFrame;
        // private Quaternion ThrottleZeroPoint;
        private bool Piloting;
        private bool Passenger;
        private float LastHitTime;
        private float PredictedHealth;
        private int ReSupplied;
        [System.NonSerializedAttribute] public float PlayerThrottle;
        [System.NonSerializedAttribute] public float VehicleSpeed;//set by syncscript if not owner
        [System.NonSerializedAttribute] public bool MovingForward;
        [System.NonSerialized] public float LastResupplyTime;
        //Quaternion VehicleRotLastFrameThrottle;
        Quaternion VehicleRotLastFrameR;
        [System.NonSerializedAttribute] public bool WheelGripLastFrameR = false;
        [System.NonSerializedAttribute] public bool WheelGrippingLastFrame_toggleR = false;
        Quaternion JoystickZeroPointR;
        Vector3 CompareAngleLastFrameR;
        private float JoystickValueLastFrameR;
        private float JoyStickValueR;
        private bool WheelGrabToggleR;
        private int WheelReleaseCountR;
        private float LastGripTimeR;
        Quaternion VehicleRotLastFrameL;
        [System.NonSerializedAttribute] public bool WheelGripLastFrameL = false;
        [System.NonSerializedAttribute] public bool WheelGrippingLastFrame_toggleL = false;
        Quaternion JoystickZeroPointL;
        Vector3 CompareAngleLastFrameL;
        private float JoystickValueLastFrameL;
        private float JoyStickValueL;
        private bool WheelGrabToggleL;
        private int WheelReleaseCountL;
        private float LastGripTimeL;
        //Vector3 CompareAngleLastFrameThrottle;
        float VRJoystickPosR = 0;
        float VRJoystickPosL = 0;
        [System.NonSerializedAttribute] public float AllGs;
        [System.NonSerializedAttribute] public Vector3 LastFrameVel = Vector3.zero;
        private float FinalThrottle;
        private Vector3 Spawnposition;
        private Quaternion Spawnrotation;
        private float AutoSteerLerper;
        [System.NonSerializedAttribute][UdonSynced(UdonSyncMode.Linear)] public float YawInput;
        [System.NonSerializedAttribute][UdonSynced(UdonSyncMode.Linear)] public float ThrottleInput;
        private VRCPlayerApi localPlayer;
        [System.NonSerializedAttribute] public bool InEditor = true;
        [System.NonSerializedAttribute] public bool Initialized = false;
        private Vector3 LastTouchedTransform_Speed = Vector3.zero;
        private Transform CenterOfMass;
        public float NumGroundedSteerWheels = 0;
        public float NumGroundedWheels = 0;
        public int NumWheels = 4;
        public float CurrentDistance;
        public bool CurrentlyDistant = true;
        float angleLast;
        int HandsOnWheel;
        [System.NonSerializedAttribute, FieldChangeCallback(nameof(HasFuel_))] public bool HasFuel = true;
        public bool HasFuel_
        {
            set
            {
                if (value)
                {
                    EntityControl.SendEventToExtensions("SFEXT_G_HasFuel");
                }
                else
                {
                    EntityControl.SendEventToExtensions("SFEXT_G_NoFuel");
                }
                HasFuel = value;
            }
            get => HasFuel;
        }
        public void SetHasFuel() { HasFuel_ = true; }
        public void SetNoFuel() { HasFuel_ = false; }
        [System.NonSerializedAttribute] public bool _DisableInput;
        [System.NonSerializedAttribute, FieldChangeCallback(nameof(DisableInput_))] public int DisableInput = 0;
        public int DisableInput_
        {
            set
            {
                if (value > 0 && DisableInput == 0)
                {
                    EntityControl.SendEventToExtensions("SFEXT_O_DisableInput_Activated");
                }
                else if (value == 0 && DisableInput > 0)
                {
                    EntityControl.SendEventToExtensions("SFEXT_O_DisableInput_Deactivated");
                }
                _DisableInput = value > 0;
                DisableInput = value;
            }
            get => DisableInput;
        }
        public void SFEXT_L_EntityStart()
        {
            if (!Initialized) { Init(); }
            if (NumStepsSec < 1f / Time.fixedDeltaTime)
            {
                Debug.LogWarning("NumStepsSec lower than FixedUpdate rate, setting it to FixedUpdate rate. Physics will be unfair in VR.");
                NumStepsSec = (int)(Mathf.Round(1f / Time.fixedDeltaTime));
            }

            NumWheels = DriveWheels.Length + SteerWheels.Length + OtherWheels.Length;

            FullHealth = Health;
            FullFuel = Fuel;

            OutsideVehicleLayer = VehicleMesh.gameObject.layer;//get the layer of the vehicle as set by the world creator

            IsOwner = EntityControl.IsOwner;
            UpdateWheelIsOwner();
            InVR = EntityControl.InVR;
            localPlayer = Networking.LocalPlayer;
            if (localPlayer == null)
            {
                InEditor = true;
            }
            else { InEditor = false; }
            Spawnposition = VehicleTransform.localPosition;
            Spawnrotation = VehicleTransform.localRotation;
            if (!ControlsRoot)
            { ControlsRoot = VehicleTransform; }
            CenterOfMass = EntityControl.CenterOfMass;
            for (int i = 0; i < DriveWheels.Length; i++)
            {
                DriveWheels[i].SetProgramVariable("IsDriveWheel", true);
            }
            for (int i = 0; i < SteerWheels.Length; i++)
            {
                SteerWheels[i].SetProgramVariable("IsSteerWheel", true);
            }
            for (int i = 0; i < OtherWheels.Length; i++)
            {
                OtherWheels[i].SetProgramVariable("IsOtherWheel", true);
            }
            if (TankMode)
            {
                for (int i = 0; i < SteerWheels.Length; i++)
                {
                    SteerWheels[i].SetProgramVariable("IsDriveWheel", true);
                }
            }
            CurrentlyDistant = true;
            SendCustomEventDelayedSeconds(nameof(CheckDistance), Random.Range(5f, 7f));//dont do all vehicles on same frame
        }
        private void Init()
        {
            Initialized = true;
            VehicleRigidbody = EntityControl.gameObject.GetComponent<Rigidbody>();
            VehicleTransform = EntityControl.transform;
            OutsideVehicleLayer = VehicleMesh.gameObject.layer;//get the layer of the vehicle as set by the world creator
        }
        private void Start()// awake function when
        {
            if (!Initialized) { Init(); }
        }
        public void ReEnableRevs()
        {
            if (Revs < RevLimiter)
            {
                LimitingRev = false;
            }
            else
            {
                SendCustomEventDelayedSeconds(nameof(ReEnableRevs), RevLimiterDelay);
            }
        }
#if UNITY_EDITOR
        public bool ACCELTEST;
#endif
        private bool[] ThrottleGripLastFrame = new bool[2];
        float[] ThrottleZeroPoint = new float[2];
        float[] TankThrottles = new float[2];
        float[] TankTempThrottles = new float[2];
        private float ThrottleSlider(float Min, float Max, bool LeftHand, float DeadZone)
        {
            int SliderIndex;
            float ThrottleGrip;
            if (LeftHand)
            {
                SliderIndex = 0;
                ThrottleGrip = Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryHandTrigger");
            }
            else
            {
                SliderIndex = 1;
                ThrottleGrip = Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryHandTrigger");
            }
            //VR Throttle
            if (ThrottleGrip > GripSensitivity)
            {
                Vector3 handdistance;
                if (LeftHand)
                { handdistance = ControlsRoot.position - localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position; }
                else
                { handdistance = ControlsRoot.position - localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position; }
                handdistance = ControlsRoot.InverseTransformDirection(handdistance);

                float HandThrottleAxis = handdistance.z;

                if (!ThrottleGripLastFrame[SliderIndex])
                {
                    if (LeftHand)
                    {
                        localPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left, .05f, .222f, 35);
                        EntityControl.SendEventToExtensions("SFEXT_O_ThrottleGrabbed_L");
                    }
                    else
                    {
                        localPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, .05f, .222f, 35);
                        EntityControl.SendEventToExtensions("SFEXT_O_ThrottleGrabbed_R");
                    }
                    ThrottleZeroPoint[SliderIndex] = HandThrottleAxis;
                    TankTempThrottles[SliderIndex] = TankThrottles[SliderIndex];
                    HandDistanceZLastFrame = 0;
                }
                float ThrottleDifference = ThrottleZeroPoint[SliderIndex] - HandThrottleAxis;
                ThrottleDifference *= ThrottleSensitivity;

                TankThrottles[SliderIndex] = Mathf.Clamp(TankTempThrottles[SliderIndex] + ThrottleDifference, Min, Max);

                HandDistanceZLastFrame = HandThrottleAxis;
                ThrottleGripLastFrame[SliderIndex] = true;
            }
            else
            {
                if (ThrottleGripLastFrame[SliderIndex])
                {
                    TankThrottles[SliderIndex] = 0;
                    if (LeftHand)
                    {
                        localPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left, .05f, .222f, 35);
                        EntityControl.SendEventToExtensions("SFEXT_O_ThrottleDropped_L");
                    }
                    else
                    {
                        localPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, .05f, .222f, 35);
                        EntityControl.SendEventToExtensions("SFEXT_O_ThrottleDropped_R");
                    }
                }
                ThrottleGripLastFrame[SliderIndex] = false;
            }
            float result = TankThrottles[SliderIndex];
            if (Mathf.Abs(result) < DeadZone)
            {
                result = 0f;
            }
            return result;
        }
        public TMPro.TextMeshPro DBGLR;
        private void LateUpdate()
        {
            float DeltaTime = Time.deltaTime;
            if (IsOwner)
            {
                if (!EntityControl._dead)
                {
                    //G/crash Damage
                    if (GDamageToTake > 0)
                    {
                        Health -= GDamageToTake * DeltaTime * GDamage;//take damage of GDamage per second per G above MaxGs
                        GDamageToTake = 0;
                    }
                    if (Health <= 0f)//vehicle is ded
                    {
                        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(Explode));
                        return;
                    }
                }
                else { GDamageToTake = 0; }
                if (!Sleeping)
                {
                    DoRepeatingWorld();
                    VehicleSpeed = VehicleVel.magnitude;
                    NumGroundedWheels = 0;
                    NumGroundedSteerWheels = 0;
                    for (int i = 0; i < SteerWheels.Length; i++)
                    {
                        if ((bool)SteerWheels[i].GetProgramVariable("Grounded"))
                        {
                            NumGroundedSteerWheels++;
                        }
                    }
                    NumGroundedWheels = NumGroundedSteerWheels;
                    for (int i = 0; i < DriveWheels.Length; i++)
                    {
                        if ((bool)DriveWheels[i].GetProgramVariable("Grounded"))
                        {
                            NumGroundedWheels++;
                        }
                    }
                    for (int i = 0; i < OtherWheels.Length; i++)
                    {
                        if ((bool)OtherWheels[i].GetProgramVariable("Grounded"))
                        {
                            NumGroundedWheels++;
                        }
                    }
                    //send grounded events
                    if (NumGroundedSteerWheels > 0)
                    {
                        if (!Grounded_Steering)
                        {
                            Grounded_Steering = true;
                            EntityControl.SendEventToExtensions("SFEXT_O_SteeringGrounded");
                        }
                    }
                    else
                    {
                        if (Grounded_Steering)
                        {
                            Grounded_Steering = false;
                            EntityControl.SendEventToExtensions("SFEXT_O_SteeringAirborne");
                        }
                    }
                    if (NumGroundedWheels > 0)
                    {
                        if (!Grounded)
                        {
                            Grounded = true;
                            EntityControl.SendEventToExtensions("SFEXT_O_Grounded");
                        }
                    }
                    else
                    {
                        if (Grounded)
                        {
                            Grounded = false;
                            EntityControl.SendEventToExtensions("SFEXT_O_Airborne");
                        }
                    }
                }
                if (Piloting)
                {
                    if (TankMode)
                    {
                        float VRThrottleL = 0;
                        float VRThrottleR = 0;
                        if (InVR)
                        {
                            VRThrottleL = ThrottleSlider(-1, 1, true, 0.02f);
                            VRThrottleR = ThrottleSlider(-1, 1, false, 0.02f);
                        }
                        int Qi = 0;
                        int Ai = 0;
                        int Ei = 0;
                        int Di = 0;
                        Qi = Input.GetKey(KeyCode.Q) ? 1 : 0;
                        Ai = Input.GetKey(KeyCode.A) ? -1 : 0;
                        Ei = Input.GetKey(KeyCode.E) ? 1 : 0;
                        Di = Input.GetKey(KeyCode.D) ? -1 : 0;

                        float LeftThrottle = Mathf.Clamp(Qi + Ai + VRThrottleL, -1, 1);
                        float RightThrottle = Mathf.Clamp(Ei + Di + VRThrottleR, -1, 1);
                        if (DBGLR) { DBGLR.text = LeftThrottle.ToString("F1") + "    " + RightThrottle.ToString("F1"); }
                        FinalThrottle = 1;//Mathf.Max(Mathf.Abs(LeftThrottle) + Mathf.Abs(RightThrottle));

                        // bool LeftNeg = LeftThrottle < 0;
                        // bool RightNeg = RightThrottle < 0;
                        // float RGearRatio = RightNeg ? -GearRatio : GearRatio;
                        // float LGearRatio = LeftNeg ? -GearRatio : GearRatio;
                        float LGearRatio = Mathf.LerpUnclamped(0, GearRatio, LeftThrottle);
                        float RGearRatio = Mathf.LerpUnclamped(0, GearRatio, RightThrottle);

                        // float LClutch = Clutch;
                        // float RClutch = Clutch;
                        // if (LeftThrottle == 0) { LClutch = 1; }
                        // if (RightThrottle == 0) { RClutch = 1; }
                        for (int i = 0; i < DriveWheels.Length; i++)
                        {
                            DriveWheels[i].SetProgramVariable("Clutch", Clutch);
                            DriveWheels[i].SetProgramVariable("_GearRatio", LGearRatio);
                        }
                        for (int i = 0; i < SteerWheels.Length; i++)
                        {
                            SteerWheels[i].SetProgramVariable("Clutch", Clutch);
                            SteerWheels[i].SetProgramVariable("_GearRatio", RGearRatio);
                        }
                    }
                    else
                    {
                        int Wi = 0;
                        int Ai = 0;
                        int Di = 0;
                        float LGrip = 0;
                        float RGrip = 0;
                        if (!_DisableInput)
                        {
                            //inputs as ints

#if UNITY_EDITOR
                            Wi = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) || ACCELTEST ? 1 : 0;
#else
                        Wi = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0;
#endif
                            //int Si = Input.GetKey(KeyCode.S) ? -1 : 0;
                            Ai = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? -1 : 0;
                            Di = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0;
                            if (!InEditor)
                            {
                                LGrip = Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryHandTrigger");
                                RGrip = Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryHandTrigger");
                            }
                        }
                        //float ThrottleGrip;
                        // if (SwitchHandsJoyThrottle)
                        // { ThrottleGrip = RGrip; }
                        // else
                        // { ThrottleGrip = LGrip; }
                        if (EnableLeaning)
                        {
                            int Threei = Input.GetKey(KeyCode.Alpha3) ? -1 : 0;
                            int Ri = Input.GetKey(KeyCode.R) ? 1 : 0;
                            float VRLean = 0;
                            float VRLeanPitch = 0;
                            if (InVR)
                            {
                                Vector3 HeadLean = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation * Vector3.up;
                                Vector3 HeadLeanRoll = Vector3.ProjectOnPlane(HeadLean, ControlsRoot.forward);
                                VRLean = Vector3.SignedAngle(HeadLeanRoll, ControlsRoot.up, ControlsRoot.forward);
                                VRLean = Mathf.Clamp(VRLean / LeanSensitivity_Roll, -1, 1);

                                Vector3 HeadOffset = ControlsRoot.position - localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                                HeadOffset = ControlsRoot.InverseTransformDirection(HeadOffset);
                                /*                Vector3 HeadLeanPitch = Vector3.ProjectOnPlane(HeadLean, ControlsRoot.right);
                                               VRLeanPitch = Vector3.SignedAngle(HeadLeanPitch, ControlsRoot.up, ControlsRoot.right);
                                               VRLeanPitch = Mathf.Clamp(VRLeanPitch / 25f, -1, 1); */
                                VRLeanPitch = Mathf.Clamp(HeadOffset.z * LeanSensitivity_Pitch, -1, 1);
                            }
                            else
                            {
                                Vector3 HeadLean = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation * Vector3.forward;
                                Vector3 HeadLeanRoll = Vector3.ProjectOnPlane(HeadLean, ControlsRoot.up);
                                VRLean = Vector3.SignedAngle(HeadLeanRoll, ControlsRoot.forward, ControlsRoot.up);
                                VRLean = -Mathf.Clamp(VRLean / 25f, -1, 1);
                            }

                            VehicleAnimator.SetFloat("lean", (VRLean * .5f) + .5f);
                            VehicleAnimator.SetFloat("leanpitch", (VRLeanPitch * .5f) + .5f);
                            VehicleRigidbody.centerOfMass = transform.InverseTransformDirection(CenterOfMass.position - transform.position);//correct position if scaled}
                        }

                        ///VR Twist Throttle
                        /*                 if (ThrottleGrip > GripSensitivity)
                                        {
                                            Quaternion VehicleRotDif = ControlsRoot.rotation * Quaternion.Inverse(VehicleRotLastFrameThrottle);//difference in vehicle's rotation since last frame
                                            VehicleRotLastFrameThrottle = ControlsRoot.rotation;
                                            ThrottleZeroPoint = VehicleRotDif * ThrottleZeroPoint;//zero point rotates with the vehicle so it appears still to the pilot
                                            if (!ThrottleGripLastFrame)//first frame you gripped Throttle
                                            {
                                                EntityControl.SendEventToExtensions("SFEXT_O_ThrottleGrabbed");
                                                VehicleRotDif = Quaternion.identity;
                                                if (SwitchHandsJoyThrottle)
                                                { ThrottleZeroPoint = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).rotation; }//rotation of the controller relative to the vehicle when it was pressed
                                                else
                                                { ThrottleZeroPoint = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).rotation; }
                                                ThrottleValue = -ThrottleInput * ThrottleDegrees;
                                                ThrottleValueLastFrame = 0;
                                                CompareAngleLastFrameThrottle = Vector3.up;
                                                ThrottleValueLastFrame = 0;
                                            }
                                            ThrottleGripLastFrame = true;
                                            //difference between the vehicle and the hand's rotation, and then the difference between that and the ThrottleZeroPoint
                                            Quaternion ThrottleDifference;
                                            ThrottleDifference = Quaternion.Inverse(ControlsRoot.rotation) *
                                                (SwitchHandsJoyThrottle ? localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).rotation
                                                                        : localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).rotation)
                                            * Quaternion.Inverse(ThrottleZeroPoint)
                                             * ControlsRoot.rotation;

                                            Vector3 ThrottlePosPitch = (ThrottleDifference * Vector3.up);
                                            Vector3 CompareAngle = Vector3.ProjectOnPlane(ThrottlePosPitch, Vector3.right);
                                            ThrottleValue += (Vector3.SignedAngle(CompareAngleLastFrameThrottle, CompareAngle, Vector3.right));
                                            CompareAngleLastFrameThrottle = CompareAngle;
                                            ThrottleValueLastFrame = ThrottleValue;
                                            VRThrottlePos = Mathf.Max(-ThrottleValue / ThrottleDegrees, 0f);
                                        }
                                        else
                                        {
                                            VRThrottlePos = 0f;
                                            if (ThrottleGripLastFrame)//first frame you let go of Throttle
                                            { EntityControl.SendEventToExtensions("SFEXT_O_ThrottleDropped"); }
                                            ThrottleGripLastFrame = false;
                                        } */
                        if (SwitchHandsJoyThrottle)
                        {
                            VRThrottlePos = Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryIndexTrigger");
                        }
                        else
                        {
                            VRThrottlePos = Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryIndexTrigger");
                        }

                        HandsOnWheel = 0;
                        if (SteeringHand_Right)
                        { RHandSteeringWheel(RGrip); }
                        if (SteeringHand_Left)
                        { LHandSteeringWheel(LGrip); }

                        float VRSteerInput = 0;
                        if (InVR)
                        {
                            if (HandsOnWheel > 0)
                            {
                                VRSteerInput = (VRJoystickPosL + VRJoystickPosR) / (float)HandsOnWheel;
                            }
                            else
                            {
                                AutoSteerLerper = YawInput;
                            }
                        }
                        float SteerInput;
                        if (UseStickSteering)
                        {
                            SteerInput = Ai + Di + Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryThumbstickHorizontal");
                        }
                        else
                        {
                            SteerInput = -VRSteerInput + Ai + Di;
                        }
                        //get the average transform movement that the steering wheels are touching
                        LastTouchedTransform_Speed = Vector3.zero;
                        for (int i = 0; i < SteerWheels.Length; i++)
                        {
                            LastTouchedTransform_Speed += (Vector3)SteerWheels[i].GetProgramVariable("LastTouchedTransform_Speed");
                        }
                        for (int i = 0; i < DriveWheels.Length; i++)
                        {
                            LastTouchedTransform_Speed += (Vector3)DriveWheels[i].GetProgramVariable("LastTouchedTransform_Speed");
                        }
                        LastTouchedTransform_Speed = LastTouchedTransform_Speed / (SteerWheels.Length + DriveWheels.Length);
                        float AutoSteer = Vector3.SignedAngle(VehicleTransform.forward, Vector3.ProjectOnPlane(VehicleVel - LastTouchedTransform_Speed, VehicleTransform.up), VehicleTransform.up);
                        if (Mathf.Abs(AutoSteer) > 110)
                        { AutoSteer = 0; }

                        { AutoSteer = Mathf.Clamp(AutoSteer / SteeringDegrees, -1, 1); }

                        float GroundedwheelsRatio = NumGroundedWheels / SteerWheels.Length;
                        if (InVR && !UseStickSteering)
                        {
                            AutoSteerLerper = Mathf.Lerp(AutoSteerLerper, AutoSteer, 1 - Mathf.Pow(0.5f, VehicleSpeed * AutoSteerStrength * GroundedwheelsRatio * DeltaTime));
                            float YawAddAmount = SteerInput;
                            if (Mathf.Abs(YawAddAmount) > 0f)
                            {
                                if (Drift_AutoSteer)
                                {
                                    YawInput = Mathf.Clamp(AutoSteerLerper + YawAddAmount, -1f, 1f);
                                }
                                else
                                {
                                    YawInput = YawAddAmount;
                                }
                            }
                            else
                            {
                                if (Drift_AutoSteer)
                                {
                                    YawInput = Mathf.Lerp(YawInput, AutoSteer, 1 - Mathf.Pow(0.5f, VehicleSpeed * AutoSteerStrength * GroundedwheelsRatio * DeltaTime));
                                }
                                else
                                {
                                    YawInput = Mathf.MoveTowards(YawInput, 0f, (1f / SteeringReturnSpeedVR) * DeltaTime);
                                }
                            }
                        }
                        else if (UseStickSteering)
                        {
                            if (SteeringMaxSpeedDTDisabled || _HandBrakeOn)//no steering limit when handbarke on
                            {
                                YawInput = Mathf.Clamp(SteerInput, -1, 1);
                            }
                            else
                            {
                                float SpeedSteeringLimitUpper = 1 - (VehicleSpeed / SteeringMaxSpeedDT);
                                SpeedSteeringLimitUpper = Mathf.Clamp(SpeedSteeringLimitUpper, DesktopMinSteering, 1);
                                float SpeedSteeringLimitLower = -SpeedSteeringLimitUpper;

                                if (AutoSteer < 0)
                                {
                                    SpeedSteeringLimitLower = Mathf.Min(SpeedSteeringLimitLower, AutoSteer - DesktopMinSteering);
                                    YawInput = SteerInput * -SpeedSteeringLimitLower;
                                }
                                else
                                {
                                    SpeedSteeringLimitUpper = Mathf.Max(SpeedSteeringLimitUpper, AutoSteer + DesktopMinSteering);
                                    YawInput = SteerInput * SpeedSteeringLimitUpper;
                                }
                            }
                        }
                        else
                        {
                            float YawAddAmount = SteerInput * DeltaTime * (1f / SteeringKeyboardSecsToMax);
                            if (YawAddAmount != 0f)
                            {
                                if (SteeringMaxSpeedDTDisabled || _HandBrakeOn)//no steering limit when handbarke on
                                {
                                    YawInput = Mathf.Clamp(YawInput + YawAddAmount, -1, 1);
                                }
                                else
                                {
                                    float SpeedSteeringLimitUpper = 1 - (VehicleSpeed / SteeringMaxSpeedDT);
                                    SpeedSteeringLimitUpper = Mathf.Clamp(SpeedSteeringLimitUpper, DesktopMinSteering, 1);
                                    float SpeedSteeringLimitLower = -SpeedSteeringLimitUpper;

                                    if (AutoSteer < 0)
                                    {
                                        SpeedSteeringLimitLower = Mathf.Min(SpeedSteeringLimitLower, AutoSteer - DesktopMinSteering);
                                    }
                                    else
                                    {
                                        SpeedSteeringLimitUpper = Mathf.Max(SpeedSteeringLimitUpper, AutoSteer + DesktopMinSteering);
                                    }
                                    YawInput = Mathf.Clamp(YawInput + YawAddAmount, SpeedSteeringLimitLower, SpeedSteeringLimitUpper);
                                }
                                if ((SteerInput > 0 && YawInput < 0) || SteerInput < 0 && YawInput > 0)
                                {
                                    YawInput = Mathf.MoveTowards(YawInput, 0f, (1f / SteeringReturnSpeedDT) * DeltaTime);
                                }
                            }
                            else
                            {
                                if (Drift_AutoSteer)
                                { YawInput = Mathf.Lerp(YawInput, AutoSteer, 1 - Mathf.Pow(0.5f, VehicleSpeed * AutoSteerStrength * DeltaTime * GroundedwheelsRatio)); }
                                else if (Bike_AutoSteer)
                                {
                                    float angle = Vector3.SignedAngle(VehicleTransform.up, Vector3.up, VehicleTransform.forward);
                                    if (angle != angleLast)
                                    {
                                        // if ((angle > 0 && YawInput < 0) || (angle < 0 && YawInput > 0))
                                        // {
                                        //     YawInput = 0;
                                        // }
                                        YawInput += angle * Bike_AutoSteer_Strength * Time.deltaTime;
                                        YawInput *= (angle - angleLast) * Bike_AutoSteer_CounterStrength * Time.deltaTime;
                                        angleLast = angle;
                                    }
                                }
                                else
                                { YawInput = Mathf.MoveTowards(YawInput, 0f, (1f / SteeringReturnSpeedDT) * DeltaTime); }
                            }
                        }
                        YawInput = Mathf.Clamp(YawInput, -1f, 1f);

                        if (InVR)
                        {
                            ThrottleInput = Mathf.Min(VRThrottlePos + Wi, 1f);
                            /*                                        else
                                               {

                                                   float ReturnSpeedGrip = 1 - Mathf.Min(ThrottleGrip / GripSensitivity, 1f);
                                                   ThrottleInput = Mathf.MoveTowards(ThrottleInput, 0f, ReturnSpeedGrip * (1f / ThrottleReturnTimeVR) * DeltaTime);
                                               } */
                        }
                        else
                        {
                            if (Wi != 0)
                            {
                                ThrottleInput = Mathf.Clamp(VRThrottlePos + (Wi), -DriveSpeedKeyboardMax, DriveSpeedKeyboardMax);
                            }
                            else
                            {
                                ThrottleInput = Mathf.MoveTowards(ThrottleInput, 0f, (1 / ThrottleReturnTimeDT) * DeltaTime);
                            }
                        }
                        for (int i = 0; i < DriveWheels.Length; i++)
                        {
                            DriveWheels[i].SetProgramVariable("Clutch", Clutch);
                            DriveWheels[i].SetProgramVariable("_GearRatio", GearRatio);
                        }
                        if (Fuel > 0)
                        {
                            if (!HasFuel_)
                            {
                                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(SetHasFuel));
                            }
                            FinalThrottle = ThrottleInput;
                            if (ThrottleInput < MinThrottle && Revs / RevLimiter < MinThrottle)
                            {
                                FinalThrottle = (MinThrottle - FinalThrottle) * MinThrottle_PStrength;
                                //P Controller for throttle
                            }
                        }
                        else
                        {
                            if (HasFuel_)
                            {
                                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(SetNoFuel));
                            }
                            FinalThrottle = 0;
                        }
                        Fuel -= Mathf.Max(FuelConsumption * Time.deltaTime * (Revs / RevLimiter), 0);
                    }
                }
            }
            else //TODO: Move this to an effects script / Have a timer to not do it while empty for more than 10s
            {
                VehicleVel = (VehicleTransform.position - VehiclePosLastFrame) / DeltaTime;
                VehicleSpeed = VehicleVel.magnitude;
                VehiclePosLastFrame = VehicleTransform.position;
                MovingForward = Vector3.Dot(VehicleTransform.forward, VehicleVel) < 0f;
            }
        }
        float Steps_Error;
        bool frame_even = true;
        private void FixedUpdate()
        {
            if (!IsOwner) { return; }
            float DeltaTime = Time.fixedDeltaTime;

            Vector3 absVel = VehicleRigidbody.velocity;
            VehicleVel = absVel - LastTouchedTransform_Speed;
            float gravity = -Physics.gravity.y * DeltaTime;
            LastFrameVel.y -= gravity; //add gravity
            AllGs = Vector3.Distance(LastFrameVel, absVel) / gravity;
            GDamageToTake += Mathf.Max((AllGs - MaxGs), 0);
            LastFrameVel = absVel;

            if (Piloting)
            {
                float StepsFloat = ((Time.fixedDeltaTime) * NumStepsSec);
                int steps = (int)((Time.fixedDeltaTime) * NumStepsSec);
                Steps_Error += StepsFloat - steps;
                if (Steps_Error > 1)
                {
                    int AddSteps = (int)Mathf.Floor(Steps_Error);//pretty sure this can never be anything but 1 unless refresh rate is changed during play maybe
                    steps += AddSteps;
                    Steps_Error = (Steps_Error - AddSteps);
                }
                if (steps < 1) { steps = 1; }//if refresh rate is above NumItsSec just run once per frame, nothing else we can do
                for (int i = 0; i < steps; i++)
                { RevUp(steps); }
            }
            else
            {
                Revs = Mathf.Max(Mathf.Lerp(Revs, 0f, 1 - Mathf.Pow(0.5f, Time.fixedDeltaTime * EngineSlowDown)), 0f);
            }
            // Alternate order of processing of wheels to make the car drive straight.
            // I don't think there's another way of fixing that without completely changing how wheel works.
            // would require communication between wheels and removal of substep?
            if (frame_even)
            {
                for (int i = 0; i < DriveWheels.Length; i++)
                { DriveWheels[i].SendCustomEvent("Wheel_FixedUpdate"); }
                for (int i = 0; i < SteerWheels.Length; i++)
                { SteerWheels[i].SendCustomEvent("Wheel_FixedUpdate"); }
                for (int i = 0; i < OtherWheels.Length; i++)
                { OtherWheels[i].SendCustomEvent("Wheel_FixedUpdate"); }
            }
            else
            {
                int numDWheels = DriveWheels.Length - 1;
                for (int i = 0; i < DriveWheels.Length; i++)
                { DriveWheels[numDWheels - i].SendCustomEvent("Wheel_FixedUpdate"); }
                int numSWheels = SteerWheels.Length - 1;
                for (int i = 0; i < SteerWheels.Length; i++)
                { SteerWheels[numSWheels - i].SendCustomEvent("Wheel_FixedUpdate"); }
                int numOWheels = OtherWheels.Length - 1;
                for (int i = 0; i < OtherWheels.Length; i++)
                { OtherWheels[numOWheels - i].SendCustomEvent("Wheel_FixedUpdate"); }
            }
            frame_even = !frame_even;

            VehicleRigidbody.velocity = Vector3.Lerp(VehicleRigidbody.velocity, Vector3.zero, 1 - Mathf.Pow(0.5f, Drag * Time.fixedDeltaTime));
        }
        private void RevUp(int NumSteps)
        {
            float PhysicsDelta = Time.fixedDeltaTime / NumSteps;
            Revs = Mathf.Max(Mathf.Lerp(Revs, 0f, 1 - Mathf.Pow(0.5f, PhysicsDelta * EngineSlowDown)), 0f);
            if (!LimitingRev)
            {
                Revs += FinalThrottle * DriveSpeed * PhysicsDelta * EngineResponseCurve.Evaluate(Revs / RevLimiter);
                if (Revs > RevLimiter)
                {
                    Revs = RevLimiter;
                    LimitingRev = true;
                    SendCustomEventDelayedSeconds(nameof(ReEnableRevs), RevLimiterDelay);
                }
            }
        }
        public void Explode()
        {
            if (EntityControl._dead) { return; }//can happen with prediction enabled if two people kill something at the same time
            EntityControl.dead = true;
            Health = FullHealth;
            HasFuel_ = true;
            if (IsOwner)
            {
                YawInput = 0;
                ThrottleInput = 0;
                Fuel = FullFuel;
                EntityControl.SendEventToExtensions("SFEXT_O_Explode");
                SendCustomEventDelayedSeconds(nameof(MoveToSpawn), RespawnDelay - 3);
            }
            EntityControl.SendEventToExtensions("SFEXT_G_Explode");

            SendCustomEventDelayedSeconds(nameof(ReAppear), RespawnDelay + Time.fixedDeltaTime * 2);
            SendCustomEventDelayedSeconds(nameof(NotDead), RespawnDelay + InvincibleAfterSpawn);
            //pilot and passengers are dropped out of the vehicle
            if ((Piloting || Passenger) && !InEditor)
            {
                EntityControl.ExitStation();
            }
        }
        public void ReAppear()
        {
            EntityControl.SendEventToExtensions("SFEXT_G_ReAppear");
            if (IsOwner)
            {
                if (!UsingManualSync)
                {
                    VehicleRigidbody.drag = 0;
                    VehicleRigidbody.angularDrag = 0;
                }
            }
        }
        public void MoveToSpawn()
        {
            PlayerThrottle = 0;//for editor test mode
            VehicleRigidbody.angularVelocity = Vector3.zero;
            VehicleRigidbody.velocity = Vector3.zero;
            //these could get set after death by lag, probably
            Health = FullHealth;
            VehicleTransform.localPosition = Spawnposition;
            VehicleTransform.localRotation = Spawnrotation;
            VehicleRigidbody.position = VehicleTransform.position;
            VehicleRigidbody.rotation = VehicleTransform.rotation;
            EntityControl.SendEventToExtensions("SFEXT_O_MoveToSpawn");
        }
        public void NotDead()
        {
            Health = FullHealth;
            EntityControl.dead = false;
        }
        [System.NonSerializedAttribute] public float FullHealth;
        //unused variables that are just here for compatability with SAV DFuncs.
        [System.NonSerialized] public int DisablePhysicsAndInputs = 0;
        [System.NonSerialized] public int DisableTaxiRotation;
        [System.NonSerialized] public int DisableGroundDetection;
        [System.NonSerialized] public int DisablePhysicsApplication;
        [System.NonSerialized] public int ThrottleOverridden;
        [System.NonSerialized] public int JoystickOverridden;
        [System.NonSerialized] public bool Taxiing = false;
        //end of compatability variables
        [System.NonSerializedAttribute] public float FullFuel;
        [System.NonSerialized] public bool Occupied;
        [System.NonSerialized] public int NumPassengers;
        [System.NonSerializedAttribute] public bool IsOwner;
        [System.NonSerializedAttribute] public bool UsingManualSync = true;
        public void SFEXT_O_RespawnButton()//called when using respawn button
        {
            VRCPlayerApi currentOwner = Networking.GetOwner(EntityControl.gameObject);
            bool BlockedCheck = (currentOwner != null && currentOwner.GetBonePosition(HumanBodyBones.Hips) == Vector3.zero) && VehicleSpeed > .2f;
            if (Occupied || EntityControl._dead || BlockedCheck) { return; }
            Networking.SetOwner(localPlayer, EntityControl.gameObject);
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(ResetStatus));
            IsOwner = true;
            Fuel = FullFuel;
            Health = FullHealth;
            YawInput = 0;
            AutoSteerLerper = 0;
            if (InEditor || UsingManualSync)
            {
                VehicleTransform.localPosition = Spawnposition;
                VehicleTransform.localRotation = Spawnrotation;
                VehicleRigidbody.position = VehicleTransform.position;
                VehicleRigidbody.rotation = VehicleTransform.rotation;
                VehicleRigidbody.velocity = Vector3.zero;
            }
            VehicleRigidbody.angularVelocity = Vector3.zero;//editor needs this
        }
        public void ResetStatus()//called globally when using respawn button
        {
            EntityControl.dead = true;
            SendCustomEventDelayedSeconds(nameof(NotDead), InvincibleAfterSpawn);
            EntityControl.SendEventToExtensions("SFEXT_G_RespawnButton");
        }
        public void SFEXT_O_TakeOwnership()
        {
            IsOwner = true;
            AllGs = 0;
            GDamageToTake = 0f;
            VehicleRigidbody.velocity = CurrentVel;
            LastFrameVel = CurrentVel;
            UpdateWheelIsOwner();
        }
        public void SFEXT_O_LoseOwnership()
        {
            VehiclePosLastFrame = VehicleTransform.position;
            IsOwner = false;
            UpdateWheelIsOwner();
        }
        public void UpdateWheelIsOwner()
        {
            if (IsOwner)
            {
                for (int i = 0; i < DriveWheels.Length; i++)
                {
                    Networking.SetOwner(Networking.LocalPlayer, DriveWheels[i].gameObject);
                }
                for (int i = 0; i < SteerWheels.Length; i++)
                {
                    Networking.SetOwner(Networking.LocalPlayer, SteerWheels[i].gameObject);
                }
                for (int i = 0; i < OtherWheels.Length; i++)
                {
                    Networking.SetOwner(Networking.LocalPlayer, OtherWheels[i].gameObject);
                }
            }
            for (int i = 0; i < DriveWheels.Length; i++)
            {
                DriveWheels[i].SendCustomEvent("UpdateOwner");
            }
            for (int i = 0; i < SteerWheels.Length; i++)
            {
                SteerWheels[i].SendCustomEvent("UpdateOwner");
            }
            for (int i = 0; i < OtherWheels.Length; i++)
            {
                OtherWheels[i].SendCustomEvent("UpdateOwner");
            }
        }
        public void SetWheelDriver()
        {
            for (int i = 0; i < DriveWheels.Length; i++)
            { DriveWheels[i].SetProgramVariable("Piloting", Piloting); }
            for (int i = 0; i < SteerWheels.Length; i++)
            { SteerWheels[i].SetProgramVariable("Piloting", Piloting); }
            for (int i = 0; i < OtherWheels.Length; i++)
            { OtherWheels[i].SetProgramVariable("Piloting", Piloting); }
        }
        /*     public void SetWheelSGV()
            {
                for (int i = 0; i < DriveWheels.Length; i++)
                {
                    DriveWheels[i].SetProgramVariable("SGVControl", this);
                }
                for (int i = 0; i < OtherWheels.Length; i++)
                {
                    OtherWheels[i].SetProgramVariable("SGVControl", this);
                }
            } */
        public void SFEXT_G_PilotEnter()
        {
            LimitingRev = false;
            Occupied = true;
        }
        public void SFEXT_G_PilotExit()
        {
            Occupied = false;
        }
        public void SFEXT_O_PilotEnter()
        {
            Piloting = true;
            GDamageToTake = 0f;
            AllGs = 0f;
            InVR = EntityControl.InVR;
            SetCollidersLayer(OnboardVehicleLayer);
            SetWheelDriver();
        }
        public void SFEXT_O_PilotExit()
        {
            Piloting = false;
            WheelGrippingLastFrame_toggleR = false;
            WheelReleaseCountR = 0;
            WheelGrabToggleR = false;
            for (int i = 0; i < DriveWheels.Length; i++)
            {
                DriveWheels[i].SetProgramVariable("EngineRevs", 0f);
                SteerWheels[i].SetProgramVariable("EngineRevs", 0f);//for TankMode
            }
            SetCollidersLayer(OutsideVehicleLayer);
            if (!EntityControl.MySeatIsExternal) { localPlayer.SetVelocity(CurrentVel); }
            SetWheelDriver();
        }
        public void SFEXT_P_PassengerEnter()
        {
            Passenger = true;
            SetCollidersLayer(OnboardVehicleLayer);
        }
        public void SFEXT_P_PassengerExit()
        {
            Passenger = false;
            if (!EntityControl.MySeatIsExternal) { localPlayer.SetVelocity(CurrentVel); }
            SetCollidersLayer(OutsideVehicleLayer);
        }
        public void SFEXT_G_PassengerEnter()
        {
            NumPassengers++;
        }
        public void SFEXT_G_PassengerExit()
        {
            NumPassengers--;
        }
        public void SFEXT_L_FallAsleep()
        {
            VehicleRigidbody.useGravity = false;
            CurrentVel = LastFrameVel = VehicleRigidbody.velocity = Vector3.zero;
            VehicleRigidbody.angularVelocity = Vector3.zero;
            VehicleSpeed = 0;
            Sleeping = true;
        }
        public void SFEXT_L_WakeUp()
        {
            VehicleRigidbody.WakeUp();
            VehicleRigidbody.useGravity = true;
            Sleeping = false;
        }
        public void SendBulletHit()
        {
            EntityControl.SendEventToExtensions("SFEXT_G_BulletHit");
        }
        public void SFEXT_L_BulletHit()
        {
            if (PredictDamage)
            {
                if (Time.time - LastHitTime > 2)
                {
                    PredictedHealth = Health - (BulletDamageTaken * EntityControl.LastHitBulletDamageMulti);
                    LastHitTime = Time.time;//must be updated before sending explode() for checks in explode event to work
                    if (PredictedHealth <= 0)
                    {
                        EntityControl.SendEventToExtensions("SFEXT_O_GunKill");
                        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(Explode));
                    }
                }
                else
                {
                    PredictedHealth -= BulletDamageTaken * EntityControl.LastHitBulletDamageMulti;
                    LastHitTime = Time.time;
                    if (PredictedHealth <= 0)
                    {
                        EntityControl.SendEventToExtensions("SFEXT_O_GunKill");
                        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(Explode));
                    }
                }
            }
        }
        public void SFEXT_G_BulletHit()
        {
            if (!EntityControl._dead)
            {
                if (IsOwner)
                {
                    Health -= BulletDamageTaken * EntityControl.LastHitBulletDamageMulti;
                    if (PredictDamage && Health <= 0)//the attacker calls the explode function in this case
                    {
                        Health = 0.0911f;
                        //if two people attacked us, and neither predicted they killed us but we took enough damage to die, we must still die.
                        SendCustomEventDelayedSeconds(nameof(CheckLaggyKilled), .25f);//give enough time for the explode event to happen if they did predict we died, otherwise do it ourself
                    }
                }
            }
        }
        public void CheckLaggyKilled()
        {
            if (!EntityControl._dead)
            {
                //Check if we still have the amount of health set to not send explode when killed, and if we do send explode
                if (Health == 0.0911f)
                {
                    SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(Explode));
                }
            }
        }
        private float LastCollisionTime;
        private float MinCollisionSoundDelay = 0.1f;
        public void SFEXT_L_OnCollisionEnter()
        {
            if (!IsOwner) { return; }
            LastCollisionTime = Time.time;
            if (Time.time - LastCollisionTime < MinCollisionSoundDelay)
            {
                LastCollisionTime = Time.time;
                Collision col = EntityControl.LastCollisionEnter;
                if (col == null) { return; }
                float colmag = col.impulse.magnitude / VehicleRigidbody.mass;
                if (colmag > BigCrashSpeed)
                {
                    SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(SendBigCrash));
                }
                else if (colmag > MediumCrashSpeed)
                {
                    SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(SendMediumCrash));
                }
                else if (colmag > SmallCrashSpeed)
                {
                    SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(SendSmallCrash));
                }
            }
        }
        public void SendSmallCrash()
        {
            EntityControl.SendEventToExtensions("SFEXT_G_SmallCrash");
        }
        public void SendMediumCrash()
        {
            EntityControl.SendEventToExtensions("SFEXT_G_MediumCrash");
        }
        public void SendBigCrash()
        {
            EntityControl.SendEventToExtensions("SFEXT_G_BigCrash");
        }
        public void SFEXT_O_ReSupply()
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(ReSupply));
        }
        public void ReSupply()
        {
            ReSupplied = 0;//used to know if other scripts resupplied
            if ((Fuel < FullFuel - 10 || Health != FullHealth))
            {
                ReSupplied++;//used to only play the sound if we're actually repairing/getting ammo/fuel
            }
            EntityControl.SendEventToExtensions("SFEXT_G_ReSupply");//extensions increase the ReSupplied value too

            LastResupplyTime = Time.time;

            if (IsOwner)
            {
                Fuel = Mathf.Min(Fuel + (FullFuel / RefuelTime), FullFuel);
                Health = Mathf.Min(Health + (FullHealth / RepairTime), FullHealth);
            }
        }
        public void CheckDistance()
        {
            CurrentDistance = Vector3.Distance(localPlayer.GetPosition(), CenterOfMass.position);
            if (CurrentDistance > DistantRange)
            {
                if (!CurrentlyDistant)
                {
                    CurrentlyDistant = true;
                    EntityControl.SendEventToExtensions("SFEXT_L_BecomeDistant");
                }
            }
            else
            {
                if (CurrentlyDistant)
                {
                    CurrentlyDistant = false;
                    EntityControl.SendEventToExtensions("SFEXT_L_NotDistant");
                }
            }
            SendCustomEventDelayedSeconds(nameof(CheckDistance), 2);
        }
        public void SetCollidersLayer(int NewLayer)
        {
            if (VehicleMesh)
            {
                if (OnlyChangeColliders)
                {
                    Collider[] children = VehicleMesh.GetComponentsInChildren<Collider>(true);
                    foreach (Collider child in children)
                    {
                        child.gameObject.layer = NewLayer;
                    }
                }
                else
                {
                    Transform[] children = VehicleMesh.GetComponentsInChildren<Transform>(true);
                    foreach (Transform child in children)
                    {
                        child.gameObject.layer = NewLayer;
                    }
                }
            }
        }
        void RHandSteeringWheel(float RGrip)
        {
            bool GrabbingR = RGrip > GripSensitivity;
            if (GrabbingR)
            {
                if (!WheelGrippingLastFrame_toggleR)
                {
                    if (Time.time - LastGripTimeR < .25f)
                    {
                        WheelGrabToggleR = true;
                        WheelReleaseCountR = 0;
                    }
                    LastGripTimeR = Time.time;
                }
                WheelGrippingLastFrame_toggleR = true;
            }
            else
            {
                if (WheelGrippingLastFrame_toggleR)
                {
                    WheelReleaseCountR++;
                    if (WheelReleaseCountR > 1)
                    {
                        WheelGrabToggleR = false;
                    }
                }
                WheelGrippingLastFrame_toggleR = false;
            }
            //VR SteeringWheel
            if (GrabbingR || WheelGrabToggleR)
            {
                //Toggle gripping the steering wheel if double tap grab
                HandsOnWheel++;
                Quaternion VehicleRotDif = ControlsRoot.rotation * Quaternion.Inverse(VehicleRotLastFrameR);//difference in vehicle's rotation since last frame
                VehicleRotLastFrameR = ControlsRoot.rotation;
                JoystickZeroPointR = VehicleRotDif * JoystickZeroPointR;//zero point rotates with the vehicle so it appears still to the pilot
                if (!WheelGripLastFrameR)//first frame you gripped joystick
                {
                    EntityControl.SendEventToExtensions("SFEXT_O_WheelGrabbedR");
                    VehicleRotDif = Quaternion.identity;
                    JoystickZeroPointR = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).rotation;
                    if (Drift_AutoSteer) { JoyStickValueR = 0; }
                    else { JoyStickValueR = -YawInput * SteeringWheelDegrees; }
                    JoystickValueLastFrameR = 0f;
                    CompareAngleLastFrameR = Vector3.up;
                    localPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, .05f, .222f, 35);
                }
                WheelGripLastFrameR = true;
                //difference between the vehicle and the hand's rotation, and then the difference between that and the JoystickZeroPoint
                Quaternion JoystickDifference;
                JoystickDifference = Quaternion.Inverse(ControlsRoot.rotation) * localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).rotation
                    * Quaternion.Inverse(JoystickZeroPointR)
                    * ControlsRoot.rotation;

                Vector3 JoystickPosYaw = (JoystickDifference * Vector3.up);
                Vector3 CompareAngle = Vector3.ProjectOnPlane(JoystickPosYaw, Vector3.forward);
                JoyStickValueR += (Vector3.SignedAngle(CompareAngleLastFrameR, CompareAngle, Vector3.forward));
                CompareAngleLastFrameR = CompareAngle;
                JoystickValueLastFrameR = JoyStickValueR;
                VRJoystickPosR = JoyStickValueR / SteeringWheelDegrees;
            }
            else
            {
                VRJoystickPosR = 0f;
                if (WheelGripLastFrameR)//first frame you let go of wheel
                {
                    EntityControl.SendEventToExtensions("SFEXT_O_WheelDroppedR");
                    WheelGripLastFrameL = false;
                    //LHandSteeringWheel hasn't run yet so don't need to do anything else
                    localPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, .05f, .222f, 35);
                }
                WheelGripLastFrameR = false;
            }
        }
        void LHandSteeringWheel(float LGrip)
        {
            //Toggle gripping the steering wheel if double tap grab
            bool GrabbingL = LGrip > GripSensitivity;
            if (GrabbingL)
            {
                if (!WheelGrippingLastFrame_toggleL)
                {
                    if (Time.time - LastGripTimeL < .25f)
                    {
                        WheelGrabToggleL = true;
                        WheelReleaseCountL = 0;
                    }
                    LastGripTimeL = Time.time;
                }
                WheelGrippingLastFrame_toggleL = true;
            }
            else
            {
                if (WheelGrippingLastFrame_toggleL)
                {
                    WheelReleaseCountL++;
                    if (WheelReleaseCountL > 1)
                    {
                        WheelGrabToggleL = false;
                    }
                }
                WheelGrippingLastFrame_toggleL = false;
            }
            //VR SteeringWheel
            if (GrabbingL || WheelGrabToggleL)
            {
                HandsOnWheel++;
                Quaternion VehicleRotDif = ControlsRoot.rotation * Quaternion.Inverse(VehicleRotLastFrameL);//difference in vehicle's rotation since last frame
                VehicleRotLastFrameL = ControlsRoot.rotation;
                JoystickZeroPointL = VehicleRotDif * JoystickZeroPointL;//zero point rotates with the vehicle so it appears still to the pilot
                if (!WheelGripLastFrameL)//first frame you gripped joystick
                {
                    EntityControl.SendEventToExtensions("SFEXT_O_WheelGrabbedL");
                    VehicleRotDif = Quaternion.identity;
                    JoystickZeroPointL = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).rotation;
                    if (Drift_AutoSteer) { JoyStickValueL = 0; }
                    else { JoyStickValueL = -YawInput * SteeringWheelDegrees; }
                    JoystickValueLastFrameL = 0f;
                    CompareAngleLastFrameL = Vector3.up;
                    localPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left, .05f, .222f, 35);
                }
                WheelGripLastFrameL = true;
                //difference between the vehicle and the hand's rotation, and then the difference between that and the JoystickZeroPointL
                Quaternion JoystickDifference = Quaternion.Inverse(ControlsRoot.rotation) * localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).rotation
                   * Quaternion.Inverse(JoystickZeroPointL)
                    * ControlsRoot.rotation;

                Vector3 JoystickPosYaw = (JoystickDifference * Vector3.up);
                Vector3 CompareAngle = Vector3.ProjectOnPlane(JoystickPosYaw, Vector3.forward);
                JoyStickValueL += (Vector3.SignedAngle(CompareAngleLastFrameL, CompareAngle, Vector3.forward));
                CompareAngleLastFrameL = CompareAngle;
                JoystickValueLastFrameL = JoyStickValueL;
                VRJoystickPosL = JoyStickValueL / SteeringWheelDegrees;
            }
            else
            {
                VRJoystickPosL = 0f;
                if (WheelGripLastFrameL)//first frame you let go of wheel
                {
                    EntityControl.SendEventToExtensions("SFEXT_O_WheelDroppedL");
                    localPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left, .05f, .222f, 35);
                    if (WheelGripLastFrameR)
                    {
                        WheelGripLastFrameR = false;
                        //regrab the right hand to stop the wheel position teleporting
                        RHandSteeringWheel(Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryHandTrigger"));
                        HandsOnWheel--;//remove one because we ran R twice
                    }
                }
                WheelGripLastFrameL = false;
            }
        }
        private bool RepeatingWorldCheckAxis;
        public void DoRepeatingWorld()
        {
            if (RepeatingWorld)
            {
                if (RepeatingWorldCheckAxis)
                {
                    if (Mathf.Abs(CenterOfMass.position.z) > RepeatingWorldDistance)
                    {
                        if (CenterOfMass.position.z > 0)
                        {
                            Vector3 vehpos = VehicleTransform.position;
                            vehpos.z -= RepeatingWorldDistance * 2;
                            VehicleTransform.position = vehpos;
                        }
                        else
                        {
                            Vector3 vehpos = VehicleTransform.position;
                            vehpos.z += RepeatingWorldDistance * 2;
                            VehicleTransform.position = vehpos;
                        }
                    }
                }
                else
                {
                    if (Mathf.Abs(CenterOfMass.position.x) > RepeatingWorldDistance)
                    {
                        if (CenterOfMass.position.x > 0)
                        {
                            Vector3 vehpos = VehicleTransform.position;
                            vehpos.x -= RepeatingWorldDistance * 2;
                            VehicleTransform.position = vehpos;
                        }
                        else
                        {
                            Vector3 vehpos = VehicleTransform.position;
                            vehpos.x += RepeatingWorldDistance * 2;
                            VehicleTransform.position = vehpos;
                        }
                    }
                }
                RepeatingWorldCheckAxis = !RepeatingWorldCheckAxis;//Check one axis per frame
            }
        }
    }
}