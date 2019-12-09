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
		private Thread rThread, iThread;
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

					//if (currentState[1] != 8)
					{
						if (currentState[1] == 0 || currentState[1] == 1 || currentState[1] == 7) Buttons |= X360Buttons.Up;
						if (currentState[1] == 4 || currentState[1] == 3 || currentState[1] == 5) Buttons |= X360Buttons.Down;
						if (currentState[1] == 6 || currentState[1] == 5 || currentState[1] == 7) Buttons |= X360Buttons.Left;
						if (currentState[1] == 2 || currentState[1] == 1 || currentState[1] == 3) Buttons |= X360Buttons.Right;
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

					short LeftStickX = (short)((Math.Max(-127.0, currentState[4] - 128) / 127) * 32767);
					if (LeftStickX == -32767)
						LeftStickX = -32768;
					
					if (LeftStickX != controller.LeftStickX)
					{
						changed = true;
						controller.LeftStickX = LeftStickX;
					}

					short LeftStickY = (short)((Math.Max(-127.0, currentState[5] - 128) / 127) * -32767);
					if (LeftStickY == -32767)
						LeftStickY = -32768;
					
					if (LeftStickY != controller.LeftStickY)
					{
						changed = true;
						controller.LeftStickY = LeftStickY;
					}

					short RightStickX = (short)((Math.Max(-127.0, currentState[6] - 128) / 127) * 32767);
					if (RightStickX == -32767)
						RightStickX = -32768;
					
					if (RightStickX != controller.RightStickX)
					{
						changed = true;
						controller.RightStickX = RightStickX;
					}

					short RightStickY = (short)((Math.Max(-127.0, currentState[7] - 128) / 127) * -32767);
					if (RightStickY == -32767)
						RightStickY = -32768;
					
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
			}
		}
	}
}
