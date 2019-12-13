using System;
using System.Threading;
using HidLibrary;
using ScpDriverInterface;

namespace StadiaToSCP
{
	public class StadiaController
	{
		public HidDevice Device { get; set; }
		public int Index;
		private Thread rThread, iThread, ssThread;
		private ScpBus ScpBus;
		private byte[] Vibration = { 0x05, 0x00, 0x00, 0x00, 0x00 };
		private Mutex rumble_mutex = new Mutex();
		private bool Running = true;
		//private byte[] enableAccelerometer = { 0x31, 0x01, 0x08 };

		public StadiaController(HidDevice device, ScpBus scpBus, int index)
		{
			Index = index;
			ScpBus = scpBus;
			Device = device;
			Device.Write(Vibration);

			rThread = new Thread(() => rumble_thread(Device));
			// rThread.Priority = ThreadPriority.BelowNormal; 
			rThread.Start();

			iThread = new Thread(() => input_thread(Device, scpBus, index));
			iThread.Priority = ThreadPriority.Highest;
			iThread.Start();
		}

		public bool check_connected()
		{
			return true;//Device.Write(Vibration);
		}

		public void unplug()
		{
			Running = false;
			rThread.Join();
			iThread.Join();
			ScpBus.Unplug(Index);
			Device.CloseDevice();
		}

		private void rumble_thread(HidDevice Device)
		{
			byte[] local_vibration = { 0x05, 0x00, 0x00, 0x00, 0x00 };
			while (Running)
			{
				rumble_mutex.WaitOne();
				if (local_vibration[3] != Vibration[3] || Vibration[1] != local_vibration[1])
				{
					local_vibration[4] = Vibration[3];
					local_vibration[3] = Vibration[3];
					local_vibration[2] = Vibration[1];
					local_vibration[1] = Vibration[1];
					rumble_mutex.ReleaseMutex();
					Device.Write(local_vibration);
					//Console.WriteLine("Small Motor: {0}, Big Motor: {1}", Vibration[3], Vibration[1]);
				}
				else
				{
					rumble_mutex.ReleaseMutex();
				}
				Thread.Sleep(20);
			}
		}

		private void input_thread(HidDevice Device, ScpBus scpBus, int index)
		{
			scpBus.PlugIn(index);
			X360Controller controller = new X360Controller();
			int timeout = 100;
			long last_changed = 0;
			long last_mi_button = 0;
			bool ss_button_pressed = false;
			bool ss_button_held = false;
			while (Running)
			{
				HidDeviceData data = Device.Read(timeout);
				var currentState = data.Data;
				bool changed = false;
				if (data.Status == HidDeviceData.ReadStatus.Success && currentState.Length >= 10 && currentState[0] == 3)
				{
					// NOTE: Console.WriteLine is blocking. If main thread sends a WriteLine while we do a WriteLine here, we're boned and will miss reports!
					//Console.WriteLine(Program.ByteArrayToHexString(currentState));
					
					X360Buttons Buttons = X360Buttons.None;
					if ((currentState[3] &  64) != 0) Buttons |= X360Buttons.A;
					if ((currentState[3] &  32) != 0) Buttons |= X360Buttons.B;
					if ((currentState[3] &  16) != 0) Buttons |= X360Buttons.X;
					if ((currentState[3] &   8) != 0) Buttons |= X360Buttons.Y;
					if ((currentState[3] &   4) != 0) Buttons |= X360Buttons.LeftBumper;
					if ((currentState[3] &   2) != 0) Buttons |= X360Buttons.RightBumper;
					if ((currentState[3] &   1) != 0) Buttons |= X360Buttons.LeftStick;
					if ((currentState[2] & 128) != 0) Buttons |= X360Buttons.RightStick;
					ss_button_pressed = ( currentState[2] & 1 ) != 0;
					// [2] & 2 == Assistant, [2] & 1 == Screenshot

					switch (currentState[1])
					{
						default:
							break;
						case 0:
							Buttons |= X360Buttons.Up;
							break;
						case 1:
							Buttons |= X360Buttons.UpRight;
							break;
						case 2:
							Buttons |= X360Buttons.Right;
							break;
						case 3:
							Buttons |= X360Buttons.DownRight;
							break;
						case 4:
							Buttons |= X360Buttons.Down;
							break;
						case 5:
							Buttons |= X360Buttons.DownLeft;
							break;
						case 6:
							Buttons |= X360Buttons.Left;
							break;
						case 7:
							Buttons |= X360Buttons.UpLeft;
							break;
					}

					if ((currentState[2] &  32) != 0) Buttons |= X360Buttons.Start;
					if ((currentState[2] &  64) != 0) Buttons |= X360Buttons.Back;

					if ((currentState[2] &  16) != 0)
					{
						last_mi_button = (DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond);
						Buttons |= X360Buttons.Logo;
					}
					if (last_mi_button != 0) Buttons |= X360Buttons.Logo;


					if (controller.Buttons != Buttons)
					{
						changed = true;
						controller.Buttons = Buttons;
					}

					// Note: The HID reports do not allow stick values of 00.
					// This seems to make sense: 0x80 is center, so usable values are:
					// 0x01 to 0x7F and 0x81 to 0xFF.
					// For our purposes I believe this is undesirable. Subtract 1 from negative
					// values to allow maxing out the stick values.
					// TODO: Get an Xbox controller and verify this is standard behavior.
					for( int i = 4; i <= 7; ++i )
					{
						if( currentState[i] <= 0x7F && currentState[i] > 0x00 )
						{
							currentState[i] -= 0x01;
						}
					}

					ushort LeftStickXunsigned = (ushort)( currentState[4] << 8 | ( currentState[4] << 1 & 255 ) );
					if (LeftStickXunsigned == 0xFFFE)
						LeftStickXunsigned = 0xFFFF;
					short LeftStickX = (short)( LeftStickXunsigned - 0x8000 );
					
					if (LeftStickX != controller.LeftStickX)
					{
						changed = true;
						controller.LeftStickX = LeftStickX;
					}

					ushort LeftStickYunsigned = (ushort)( currentState[5] << 8 | ( currentState[5] << 1 & 255 ) );
					if (LeftStickYunsigned == 0xFFFE)
						LeftStickYunsigned = 0xFFFF;
					short LeftStickY = (short)( -LeftStickYunsigned + 0x7FFF );
					if (LeftStickY == -1)
						LeftStickY = 0;
					if (LeftStickY != controller.LeftStickY)
					{
						changed = true;
						controller.LeftStickY = LeftStickY;
					}

					ushort RightStickXunsigned = (ushort)( currentState[6] << 8 | ( currentState[6] << 1 & 255 ) );
					if (RightStickXunsigned == 0xFFFE)
						RightStickXunsigned = 0xFFFF;
					short RightStickX = (short)( RightStickXunsigned - 0x8000 );
					
					if (RightStickX != controller.RightStickX)
					{
						changed = true;
						controller.RightStickX = RightStickX;
					}

					ushort RightStickYunsigned = (ushort)( currentState[7] << 8 | ( currentState[7] << 1 & 255 ) );
					if (RightStickYunsigned == 0xFFFE)
						RightStickYunsigned = 0xFFFF;
					short RightStickY = (short)( -RightStickYunsigned + 0x7FFF );
					if (RightStickY == -1)
						RightStickY = 0;
					
					if (RightStickY != controller.RightStickY)
					{
						changed = true;
						controller.RightStickY = RightStickY;
					}

					if (controller.LeftTrigger != currentState[8])
					{
						changed = true;
						controller.LeftTrigger = currentState[8];
					}

					if (controller.RightTrigger != currentState[9])
					{
						changed = true;
						controller.RightTrigger = currentState[9];
					}
				}

				if (data.Status == HidDeviceData.ReadStatus.WaitTimedOut || (!changed && ((last_changed + timeout) < (DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond))))
				{
					changed = true;
				}

				if (changed)
				{
					//Console.WriteLine("changed");
					//Console.WriteLine((DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond));
					byte[] outputReport = new byte[8];
					scpBus.Report(index, controller.GetReport(), outputReport);

					if (outputReport[1] == 0x08)
					{
						byte bigMotor = outputReport[3];
						byte smallMotor = outputReport[4];
						rumble_mutex.WaitOne();
						if (smallMotor != Vibration[3] || Vibration[1] != bigMotor)
						{
							Vibration[1] = bigMotor;
							Vibration[3] = smallMotor;
						}
						rumble_mutex.ReleaseMutex();
					}

					if (last_mi_button != 0)
					{
						if ((last_mi_button + 100) < (DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond))
						{
							last_mi_button = 0;
							controller.Buttons ^= X360Buttons.Logo;
						}
					}

					last_changed = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
				}

				if (ss_button_pressed && !ss_button_held)
				{
					ss_button_held = true;
					try
					{
						// TODO: Allow configuring this keybind.
						ssThread = new Thread( () => System.Windows.Forms.SendKeys.SendWait( "^+Z" ) );
						ssThread.Start();
					}
					catch
					{
					}
				}
				else if (ss_button_held && !ss_button_pressed)
				{
					ss_button_held = false;
				}
			}
		}
	}
}
