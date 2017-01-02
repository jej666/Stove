using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

using Autofac.Extras.IocManager;

using Stove.Extensions;
using Stove.Runtime.Session;

namespace Stove.Domain.Uow
{
    /// <summary>
    ///     Base for all Unit Of Work classes.
    /// </summary>
    public abstract class UnitOfWorkBase : IUnitOfWork
    {
        private readonly List<DataFilterConfiguration> _filters;

        /// <summary>
        ///     A reference to the exception if this unit of work failed.
        /// </summary>
        private Exception _exception;

        /// <summary>
        ///     Is <see cref="Begin" /> method called before?
        /// </summary>
        private bool _isBeginCalledBefore;

        /// <summary>
        ///     Is <see cref="Complete" /> method called before?
        /// </summary>
        private bool _isCompleteCalledBefore;

        /// <summary>
        ///     Is this unit of work successfully completed.
        /// </summary>
        private bool _succeed;

        /// <summary>
        ///     Constructor.
        /// </summary>
        protected UnitOfWorkBase(
            IConnectionStringResolver connectionStringResolver,
            IUnitOfWorkDefaultOptions defaultOptions,
            IUnitOfWorkFilterExecuter filterExecuter)
        {
            FilterExecuter = filterExecuter;
            DefaultOptions = defaultOptions;
            ConnectionStringResolver = connectionStringResolver;

            Id = Guid.NewGuid().ToString("N");
            _filters = defaultOptions.Filters.ToList();

            StoveSession = NullStoveSession.Instance;
        }

        /// <summary>
        ///     Gets default UOW options.
        /// </summary>
        protected IUnitOfWorkDefaultOptions DefaultOptions { get; }

        /// <summary>
        ///     Gets the connection string resolver.
        /// </summary>
        protected IConnectionStringResolver ConnectionStringResolver { get; }

        /// <summary>
        ///     Reference to current Stove session.
        /// </summary>
        public IStoveSession StoveSession { protected get; set; }

        protected IUnitOfWorkFilterExecuter FilterExecuter { get; }

        public string Id { get; }

        [DoNotInject]
        public IUnitOfWork Outer { get; set; }

        /// <inheritdoc />
        public event EventHandler Completed;

        /// <inheritdoc />
        public event EventHandler<UnitOfWorkFailedEventArgs> Failed;

        /// <inheritdoc />
        public event EventHandler Disposed;

        /// <inheritdoc />
        public UnitOfWorkOptions Options { get; private set; }

        /// <inheritdoc />
        public IReadOnlyList<DataFilterConfiguration> Filters
        {
            get { return _filters.ToImmutableList(); }
        }

        /// <summary>
        ///     Gets a value indicates that this unit of work is disposed or not.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc />
        public void Begin(UnitOfWorkOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            PreventMultipleBegin();
            Options = options; //TODO: Do not set options like that, instead make a copy?

            SetFilters(options.FilterOverrides);

            BeginUow();
        }

        /// <inheritdoc />
        public abstract void SaveChanges();

        /// <inheritdoc />
        public abstract Task SaveChangesAsync();

        /// <inheritdoc />
        public IDisposable DisableFilter(params string[] filterNames)
        {
            //TODO: Check if filters exists?

            var disabledFilters = new List<string>();

            foreach (string filterName in filterNames)
            {
                int filterIndex = GetFilterIndex(filterName);
                if (_filters[filterIndex].IsEnabled)
                {
                    disabledFilters.Add(filterName);
                    _filters[filterIndex] = new DataFilterConfiguration(_filters[filterIndex], false);
                }
            }

            disabledFilters.ForEach(ApplyDisableFilter);

            return new DisposeAction(() => EnableFilter(disabledFilters.ToArray()));
        }

        /// <inheritdoc />
        public IDisposable EnableFilter(params string[] filterNames)
        {
            //TODO: Check if filters exists?

            var enabledFilters = new List<string>();

            foreach (string filterName in filterNames)
            {
                int filterIndex = GetFilterIndex(filterName);
                if (!_filters[filterIndex].IsEnabled)
                {
                    enabledFilters.Add(filterName);
                    _filters[filterIndex] = new DataFilterConfiguration(_filters[filterIndex], true);
                }
            }

            enabledFilters.ForEach(ApplyEnableFilter);

            return new DisposeAction(() => DisableFilter(enabledFilters.ToArray()));
        }

        /// <inheritdoc />
        public bool IsFilterEnabled(string filterName)
        {
            return GetFilter(filterName).IsEnabled;
        }

        /// <inheritdoc />
        public IDisposable SetFilterParameter(string filterName, string parameterName, object value)
        {
            int filterIndex = GetFilterIndex(filterName);

            var newfilter = new DataFilterConfiguration(_filters[filterIndex]);

            //Store old value
            object oldValue = null;
            bool hasOldValue = newfilter.FilterParameters.ContainsKey(parameterName);
            if (hasOldValue)
            {
                oldValue = newfilter.FilterParameters[parameterName];
            }

            newfilter.FilterParameters[parameterName] = value;

            _filters[filterIndex] = newfilter;

            ApplyFilterParameterValue(filterName, parameterName, value);

            return new DisposeAction(() =>
            {
                //Restore old value
                if (hasOldValue)
                {
                    SetFilterParameter(filterName, parameterName, oldValue);
                }
            });
        }

        /// <inheritdoc />
        public void Complete()
        {
            PreventMultipleComplete();
            try
            {
                CompleteUow();
                _succeed = true;
                OnCompleted();
            }
            catch (Exception ex)
            {
                _exception = ex;
                throw;
            }
        }

        /// <inheritdoc />
        public async Task CompleteAsync()
        {
            PreventMultipleComplete();
            try
            {
                await CompleteUowAsync();
                _succeed = true;
                OnCompleted();
            }
            catch (Exception ex)
            {
                _exception = ex;
                throw;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;

            if (!_succeed)
            {
                OnFailed(_exception);
                Rollback();
            }

            DisposeUow();
            OnDisposed();
        }

        public virtual void Rollback()
        {
        }

        /// <summary>
        ///     Can be implemented by derived classes to start UOW.
        /// </summary>
        protected virtual void BeginUow()
        {
        }

        /// <summary>
        ///     Should be implemented by derived classes to complete UOW.
        /// </summary>
        protected abstract void CompleteUow();

        /// <summary>
        ///     Should be implemented by derived classes to complete UOW.
        /// </summary>
        protected abstract Task CompleteUowAsync();

        /// <summary>
        ///     Should be implemented by derived classes to dispose UOW.
        /// </summary>
        protected abstract void DisposeUow();

        protected virtual void ApplyDisableFilter(string filterName)
        {
            FilterExecuter.ApplyDisableFilter(this, filterName);
        }

        protected virtual void ApplyEnableFilter(string filterName)
        {
            FilterExecuter.ApplyEnableFilter(this, filterName);
        }

        protected virtual void ApplyFilterParameterValue(string filterName, string parameterName, object value)
        {
            FilterExecuter.ApplyFilterParameterValue(this, filterName, parameterName, value);
        }

        protected virtual string ResolveConnectionString()
        {
            return ConnectionStringResolver.GetNameOrConnectionString();
        }

        /// <summary>
        ///     Called to trigger <see cref="Completed" /> event.
        /// </summary>
        protected virtual void OnCompleted()
        {
            Completed.InvokeSafely(this);
        }

        /// <summary>
        ///     Called to trigger <see cref="Failed" /> event.
        /// </summary>
        /// <param name="exception">Exception that cause failure</param>
        protected virtual void OnFailed(Exception exception)
        {
            Failed.InvokeSafely(this, new UnitOfWorkFailedEventArgs(exception));
        }

        /// <summary>
        ///     Called to trigger <see cref="Disposed" /> event.
        /// </summary>
        protected virtual void OnDisposed()
        {
            Disposed.InvokeSafely(this);
        }

        private void PreventMultipleBegin()
        {
            if (_isBeginCalledBefore)
            {
                throw new StoveException("This unit of work has started before. Can not call Start method more than once.");
            }

            _isBeginCalledBefore = true;
        }

        private void PreventMultipleComplete()
        {
            if (_isCompleteCalledBefore)
            {
                throw new StoveException("Complete is called before!");
            }

            _isCompleteCalledBefore = true;
        }

        private void SetFilters(List<DataFilterConfiguration> filterOverrides)
        {
            for (var i = 0; i < _filters.Count; i++)
            {
                DataFilterConfiguration filterOverride = filterOverrides.FirstOrDefault(f => f.FilterName == _filters[i].FilterName);
                if (filterOverride != null)
                {
                    _filters[i] = filterOverride;
                }
            }
        }

        private void ChangeFilterIsEnabledIfNotOverrided(List<DataFilterConfiguration> filterOverrides, string filterName, bool isEnabled)
        {
            if (filterOverrides.Any(f => f.FilterName == filterName))
            {
                return;
            }

            int index = _filters.FindIndex(f => f.FilterName == filterName);
            if (index < 0)
            {
                return;
            }

            if (_filters[index].IsEnabled == isEnabled)
            {
                return;
            }

            _filters[index] = new DataFilterConfiguration(filterName, isEnabled);
        }

        private DataFilterConfiguration GetFilter(string filterName)
        {
            DataFilterConfiguration filter = _filters.FirstOrDefault(f => f.FilterName == filterName);
            if (filter == null)
            {
                throw new StoveException("Unknown filter name: " + filterName + ". Be sure this filter is registered before.");
            }

            return filter;
        }

        private int GetFilterIndex(string filterName)
        {
            int filterIndex = _filters.FindIndex(f => f.FilterName == filterName);
            if (filterIndex < 0)
            {
                throw new StoveException("Unknown filter name: " + filterName + ". Be sure this filter is registered before.");
            }

            return filterIndex;
        }
    }
}
