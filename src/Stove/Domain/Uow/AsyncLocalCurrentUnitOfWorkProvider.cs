﻿using System.Threading;

using Autofac.Extras.IocManager;

using Stove.Log;

namespace Stove.Domain.Uow
{
	/// <summary>
	///     CallContext implementation of <see cref="ICurrentUnitOfWorkProvider" />.
	///     This is the default implementation.
	/// </summary>
	public class AsyncLocalCurrentUnitOfWorkProvider : ICurrentUnitOfWorkProvider, ITransientDependency
	{
		private static readonly AsyncLocal<LocalUowWrapper> AsyncLocalUow = new AsyncLocal<LocalUowWrapper>();

		public AsyncLocalCurrentUnitOfWorkProvider()
		{
			Logger = NullLogger.Instance;
		}

		public ILogger Logger { get; set; }

		/// <inheritdoc />
		[DoNotInject]
		public IUnitOfWork Current
		{
			get { return GetCurrentUow(); }
			set { SetCurrentUow(value); }
		}

		private static IUnitOfWork GetCurrentUow()
		{
			var uow = AsyncLocalUow.Value?.UnitOfWork;
			if (uow == null)
			{
				return null;
			}

			if (uow.IsDisposed)
			{
				AsyncLocalUow.Value = null;
				return null;
			}

			return uow;
		}

		private static void SetCurrentUow(IUnitOfWork value)
		{
			lock (AsyncLocalUow)
			{
				if (value == null)
				{
					if (AsyncLocalUow.Value == null)
					{
						return;
					}

					if (AsyncLocalUow.Value.UnitOfWork?.Outer == null)
					{
						AsyncLocalUow.Value.UnitOfWork = null;
						AsyncLocalUow.Value = null;
						return;
					}

					AsyncLocalUow.Value.UnitOfWork = AsyncLocalUow.Value.UnitOfWork.Outer;
				}
				else
				{
					if (AsyncLocalUow.Value?.UnitOfWork == null)
					{
						if (AsyncLocalUow.Value != null)
						{
							AsyncLocalUow.Value.UnitOfWork = value;
						}

						AsyncLocalUow.Value = new LocalUowWrapper(value);
						return;
					}

					value.Outer = AsyncLocalUow.Value.UnitOfWork;
					AsyncLocalUow.Value.UnitOfWork = value;
				}
			}
		}

		private class LocalUowWrapper
		{
			public LocalUowWrapper(IUnitOfWork unitOfWork)
			{
				UnitOfWork = unitOfWork;
			}

			public IUnitOfWork UnitOfWork { get; set; }
		}
	}
}
