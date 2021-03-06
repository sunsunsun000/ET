﻿using System;
using System.Collections.Generic;
using Model;

namespace Hotfix
{
	public interface IObjectSystem
	{
		Type Type();
		void Set(object value);
	}

	public abstract class ObjectSystem<T> : IObjectSystem
	{
		private T value;

		protected T Get()
		{
			return value;
		}

		public void Set(object v)
		{
			this.value = (T)v;
		}

		public Type Type()
		{
			return typeof(T);
		}
	}

	public sealed class EventSystem
	{
		private readonly Dictionary<Type, IObjectSystem> disposerEvents = new Dictionary<Type, IObjectSystem>();

		private readonly Dictionary<int, List<IEvent>> allEvents = new Dictionary<int, List<IEvent>>();

		private Queue<Component> updates = new Queue<Component>();
		private Queue<Component> updates2 = new Queue<Component>();

		private readonly Queue<Component> starts = new Queue<Component>();

		private Queue<Component> loaders = new Queue<Component>();
		private Queue<Component> loaders2 = new Queue<Component>();

		private Queue<Component> lateUpdates = new Queue<Component>();
		private Queue<Component> lateUpdates2 = new Queue<Component>();

		private readonly HashSet<Component> unique = new HashSet<Component>();

		public EventSystem()
		{
			this.disposerEvents.Clear();

			Type[] types = DllHelper.GetHotfixTypes();
			foreach (Type type in types)
			{
				object[] attrs = type.GetCustomAttributes(typeof(ObjectSystemAttribute), false);

				if (attrs.Length == 0)
				{
					continue;
				}

				object obj = Activator.CreateInstance(type);
				IObjectSystem objectSystem = obj as IObjectSystem;
				if (objectSystem == null)
				{
					Log.Error($"组件事件没有继承IObjectEvent: {type.Name}");
					continue;
				}
				this.disposerEvents[objectSystem.Type()] = objectSystem;
			}

			this.allEvents.Clear();
			foreach (Type type in types)
			{
				object[] attrs = type.GetCustomAttributes(typeof(EventAttribute), false);

				foreach (object attr in attrs)
				{
					EventAttribute aEventAttribute = (EventAttribute)attr;
					object obj = Activator.CreateInstance(type);
					IEvent iEvent = obj as IEvent;
					if (iEvent == null)
					{
						Log.Error($"{obj.GetType().Name} 没有继承IEvent");
					}
					this.RegisterEvent(aEventAttribute.Type, iEvent);

					// hotfix的事件也要注册到mono层，hotfix可以订阅mono层的事件
					Action<List<object>> action = list => { Handle(aEventAttribute.Type, list); };
					Game.EventSystem.RegisterEvent(aEventAttribute.Type, new EventProxy(action));
				}
			}

			this.Load();
		}

		public void RegisterEvent(int eventId, IEvent e)
		{
			if (!this.allEvents.ContainsKey(eventId))
			{
				this.allEvents.Add(eventId, new List<IEvent>());
			}
			this.allEvents[eventId].Add(e);
		}

		public static void Handle(int type, List<object> param)
		{
			switch (param.Count)
			{
				case 0:
					Hotfix.EventSystem.Run(type);
					break;
				case 1:
					Hotfix.EventSystem.Run(type, param[0]);
					break;
				case 2:
					Hotfix.EventSystem.Run(type, param[0], param[1]);
					break;
				case 3:
					Hotfix.EventSystem.Run(type, param[0], param[1], param[2]);
					break;
			}
		}

		public void Add(Component disposer)
		{
			if (!this.disposerEvents.TryGetValue(disposer.GetType(), out IObjectSystem objectSystem))
			{
				return;
			}

			if (objectSystem is ILoad)
			{
				this.loaders.Enqueue(disposer);
			}

			if (objectSystem is IUpdate)
			{
				this.updates.Enqueue(disposer);
			}

			if (objectSystem is IStart)
			{
				this.starts.Enqueue(disposer);
			}

			if (objectSystem is ILateUpdate)
			{
				this.lateUpdates.Enqueue(disposer);
			}
		}

		public void Awake(Component disposer)
		{
			this.Add(disposer);

			if (!this.disposerEvents.TryGetValue(disposer.GetType(), out IObjectSystem objectEvent))
			{
				return;
			}
			IAwake iAwake = objectEvent as IAwake;
			if (iAwake == null)
			{
				return;
			}
			objectEvent.Set(disposer);
			iAwake.Awake();
		}

		public void Awake<P1>(Component disposer, P1 p1)
		{
			this.Add(disposer);

			if (!this.disposerEvents.TryGetValue(disposer.GetType(), out IObjectSystem objectEvent))
			{
				throw new Exception($"{disposer.GetType().Name} not found awake1");
			}
			IAwake<P1> iAwake = objectEvent as IAwake<P1>;
			if (iAwake == null)
			{
				throw new Exception($"{disposer.GetType().Name} not found awake1");
			}
			objectEvent.Set(disposer);
			iAwake.Awake(p1);
		}

		public void Awake<P1, P2>(Component disposer, P1 p1, P2 p2)
		{
			this.Add(disposer);

			if (!this.disposerEvents.TryGetValue(disposer.GetType(), out IObjectSystem objectEvent))
			{
				throw new Exception($"{disposer.GetType().Name} not found awake2");
			}
			IAwake<P1, P2> iAwake = objectEvent as IAwake<P1, P2>;
			if (iAwake == null)
			{
				throw new Exception($"{disposer.GetType().Name} not found awake2");
			}
			objectEvent.Set(disposer);
			iAwake.Awake(p1, p2);
		}

		public void Awake<P1, P2, P3>(Component disposer, P1 p1, P2 p2, P3 p3)
		{
			this.Add(disposer);

			if (!this.disposerEvents.TryGetValue(disposer.GetType(), out IObjectSystem objectEvent))
			{
				throw new Exception($"{disposer.GetType().Name} not found awake3");
			}
			IAwake<P1, P2, P3> iAwake = objectEvent as IAwake<P1, P2, P3>;
			if (iAwake == null)
			{
				throw new Exception($"{disposer.GetType().Name} not found awake3");
			}
			objectEvent.Set(disposer);
			iAwake.Awake(p1, p2, p3);
		}

		public void Load()
		{
			unique.Clear();
			while (this.loaders.Count > 0)
			{
				Component disposer = this.loaders.Dequeue();
				if (disposer.IsDisposed)
				{
					continue;
				}

				if (!this.unique.Add(disposer))
				{
					continue;
				}

				if (!this.disposerEvents.TryGetValue(disposer.GetType(), out IObjectSystem objectEvent))
				{
					continue;
				}

				this.loaders2.Enqueue(disposer);

				ILoad iLoad = objectEvent as ILoad;
				if (iLoad == null)
				{
					continue;
				}
				objectEvent.Set(disposer);
				try
				{
					iLoad.Load();
				}
				catch (Exception e)
				{
					Log.Error(e.ToString());
				}
			}

			ObjectHelper.Swap(ref this.loaders, ref this.loaders2);
		}

		private void Start()
		{
			unique.Clear();
			while (this.starts.Count > 0)
			{
				Component disposer = this.starts.Dequeue();

				if (!this.unique.Add(disposer))
				{
					continue;
				}

				if (!this.disposerEvents.TryGetValue(disposer.GetType(), out IObjectSystem objectEvent))
				{
					continue;
				}
				IStart iStart = objectEvent as IStart;
				if (iStart == null)
				{
					continue;
				}
				objectEvent.Set(disposer);
				iStart.Start();
			}
		}

		public void Update()
		{
			this.Start();

			unique.Clear();
			while (this.updates.Count > 0)
			{
				Component disposer = this.updates.Dequeue();
				if (disposer.IsDisposed)
				{
					continue;
				}

				if (!this.unique.Add(disposer))
				{
					continue;
				}

				if (!this.disposerEvents.TryGetValue(disposer.GetType(), out IObjectSystem objectEvent))
				{
					continue;
				}

				this.updates2.Enqueue(disposer);

				IUpdate iUpdate = objectEvent as IUpdate;
				if (iUpdate == null)
				{
					continue;
				}
				objectEvent.Set(disposer);
				try
				{
					iUpdate.Update();
				}
				catch (Exception e)
				{
					Log.Error(e.ToString());
				}
			}

			ObjectHelper.Swap(ref this.updates, ref this.updates2);
		}

		public void LateUpdate()
		{
			unique.Clear();
			while (this.lateUpdates.Count > 0)
			{
				Component disposer = this.lateUpdates.Dequeue();
				if (disposer.IsDisposed)
				{
					continue;
				}

				if (!this.unique.Add(disposer))
				{
					continue;
				}

				IObjectSystem objectSystem;
				if (!this.disposerEvents.TryGetValue(disposer.GetType(), out objectSystem))
				{
					continue;
				}

				this.lateUpdates2.Enqueue(disposer);

				ILateUpdate iLateUpdate = objectSystem as ILateUpdate;
				if (iLateUpdate == null)
				{
					continue;
				}
				objectSystem.Set(disposer);
				try
				{
					iLateUpdate.LateUpdate();
				}
				catch (Exception e)
				{
					Log.Error(e.ToString());
				}
			}

			ObjectHelper.Swap(ref this.lateUpdates, ref this.lateUpdates2);
		}

		public void Run(int type)
		{
			List<IEvent> iEvents;
			if (!this.allEvents.TryGetValue((int)type, out iEvents))
			{
				return;
			}
			foreach (IEvent iEvent in iEvents)
			{
				try
				{
					iEvent?.Handle();
				}
				catch (Exception e)
				{
					Log.Error(e.ToString());
				}
			}
		}

		public void Run<A>(int type, A a)
		{
			List<IEvent> iEvents;
			if (!this.allEvents.TryGetValue((int)type, out iEvents))
			{
				return;
			}
			foreach (IEvent iEvent in iEvents)
			{
				try
				{
					iEvent?.Handle(a);
				}
				catch (Exception e)
				{
					Log.Error(e.ToString());
				}
			}
		}

		public void Run<A, B>(int type, A a, B b)
		{
			List<IEvent> iEvents;
			if (!this.allEvents.TryGetValue((int)type, out iEvents))
			{
				return;
			}
			foreach (IEvent iEvent in iEvents)
			{
				try
				{
					iEvent?.Handle(a, b);
				}
				catch (Exception e)
				{
					Log.Error(e.ToString());
				}
			}
		}

		public void Run<A, B, C>(int type, A a, B b, C c)
		{
			List<IEvent> iEvents;
			if (!this.allEvents.TryGetValue((int)type, out iEvents))
			{
				return;
			}
			foreach (IEvent iEvent in iEvents)
			{
				try
				{
					iEvent?.Handle(a, b, c);
				}
				catch (Exception e)
				{
					Log.Error(e.ToString());
				}
			}
		}
	}
}