﻿using Orleans;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using GrainInterfaces;

namespace Grains.Redux
{
    public abstract class ReduxGrain<TState> : Grain, IReduxGrain<TState> where TState: class, new()
    {
        protected ReduxGrainStore<TState> Store { get; private set; }

        private readonly Reducer<TState> reducer;
        private readonly ReduxTableStorage<TState> storage;
        private string tableKey;

        public ReduxGrain(Reducer<TState> reducer, ReduxTableStorage<TState> storage)
        {
            this.reducer = reducer;
            this.storage = storage;
        }

        public override Task OnActivateAsync()
        {
            this.tableKey = this.GetKeyString();
            return Task.WhenAll(
                this.ReadStateAsync(),
                base.OnActivateAsync()
                );
        }

        private string GetKeyString()
        {
            string keyExt;
            var key = this.GetPrimaryKey(out keyExt);
            return (key.ToString() + "." + keyExt)
                .Replace('/', '_')        // Forward slash
                .Replace('+', '-');       // Backslash
        }

        public async Task WriteStateAsync()
        {
            var actionList = Store.GetState().UnsavedActions;
            await storage.WriteAsync(this.tableKey, actionList, this.GetLogger());
            Store.GetState().UnsavedActions.Clear();
        }

        // Overwrites the Store, canceling potential subscriptions, therefore marked private
        private async Task ReadStateAsync()
        {
            var state = Tuple.Create<TState, uint>(null, 0);
            var storageActonObservable = storage.ReadObservable(this.tableKey, this.GetLogger());
            state = await storageActonObservable.Aggregate(state, (s, a) => 
                {
                    s = Tuple.Create(this.reducer(s.Item1, a.Action), a.Serial);
                    return s;
                });
            this.Store = new ReduxGrainStore<TState>(this.reducer, state.Item1, state.Item2);
        }

        public Task<IAction> Dispatch(IAction action)
        {
            return Task.FromResult(this.Store.Dispatch(action));
        }

        public Task<TState> GetState()
        {
            return Task.FromResult(Store.State);
        }
    }
}
