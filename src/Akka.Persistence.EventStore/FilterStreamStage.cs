using System;
using Akka.Persistence.EventStore.Query;
using Akka.Streams;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;

namespace Akka.Persistence.EventStore;

public sealed class FilterStreamStage(EventStoreQueryFilter filter) : SimpleLinearGraphStage<ReplayCompletion>
{
    #region Logic

    private sealed class Logic : InAndOutGraphStageLogic
    {
        private readonly FilterStreamStage _stage;
        private readonly Decider _decider;

        public Logic(FilterStreamStage stage, Attributes inheritedAttributes) : base(stage.Shape)
        {
            _stage = stage;
            var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy?>(null);
            _decider = attr != null ? attr.Decider : Deciders.StoppingDecider;

            SetHandler(stage.Inlet, this);
            SetHandler(stage.Outlet, this);
        }

        public override void OnPush()
        {
            try
            {
                var element = Grab(_stage.Inlet);
                
                var filterResult = _stage._filter.Filter(element.Event);

                switch (filterResult)
                {
                    case EventStoreQueryFilter.StreamContinuation.Skip:
                        Pull(_stage.Inlet);
                        break;
                    case EventStoreQueryFilter.StreamContinuation.Include:
                        Push(_stage.Outlet, element);
                        break;
                    case EventStoreQueryFilter.StreamContinuation.Complete:
                        Complete(_stage.Outlet);
                        break;
                    case EventStoreQueryFilter.StreamContinuation.IncludeThenComplete:
                        Push(_stage.Outlet, element);
                        Complete(_stage.Outlet);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            catch (Exception ex)
            {
                if (_decider(ex) == Directive.Stop)
                    FailStage(ex);
                else
                    Pull(_stage.Inlet);
            }
        }

        public override void OnPull() => Pull(_stage.Inlet);

        public override string ToString() => "FilterStreamStageLogic";
    }

    #endregion

    private readonly EventStoreQueryFilter _filter = filter;

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this, inheritedAttributes);

    public override string ToString() => "FilterStreamStage";
}