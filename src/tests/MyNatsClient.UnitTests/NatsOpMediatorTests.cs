using System;
using System.Reactive;
using FluentAssertions;
using Moq;
using MyNatsClient.Ops;
using Xunit;

namespace MyNatsClient.UnitTests
{
    public class NatsOpMediatorTests : UnitTestsOf<NatsOpMediator>
    {
        public NatsOpMediatorTests()
        {
            UnitUnderTest = new NatsOpMediator();
        }

        [Fact]
        public void Dispatching_Should_update_date_time_for_last_received_op()
        {
            var op = Mock.Of<IOp>();
            UnitUnderTest.LastOpReceivedAt.Should().Be(DateTime.MinValue);

            UnitUnderTest.Dispatch(op);

            UnitUnderTest.LastOpReceivedAt.Should().BeCloseTo(DateTime.UtcNow);
        }

        [Fact]
        public void Dispatching_Should_update_op_count()
        {
            var op = Mock.Of<IOp>();

            UnitUnderTest.Dispatch(op);
            UnitUnderTest.Dispatch(op);

            UnitUnderTest.OpCount.Should().Be(2);
        }

        [Fact]
        public void Dispatching_MsgOp_Should_dispatch_to_both_AllOpsStream_and_MsgOpsStream()
        {
            var msgOp = new MsgOp("TestSubject", "0a3282e769e34677809db5d756dfd768", new byte[0]);
            var opStreamRec = false;
            var msgOpStreamRec = false;
            UnitUnderTest.AllOpsStream.Subscribe(new AnonymousObserver<IOp>(op => opStreamRec = true));
            UnitUnderTest.MsgOpsStream.Subscribe(new AnonymousObserver<MsgOp>(op => msgOpStreamRec = true));

            UnitUnderTest.Dispatch(msgOp);

            opStreamRec.Should().BeTrue();
            msgOpStreamRec.Should().BeTrue();
        }

        [Fact]
        public void Dispatching_non_MsgOp_Should_not_dispatch_to_MsgOpsStream_but_AllOpsStream()
        {
            var opStreamRec = false;
            var msgOpStreamRec = false;
            UnitUnderTest.AllOpsStream.Subscribe(new AnonymousObserver<IOp>(op => opStreamRec = true));
            UnitUnderTest.MsgOpsStream.Subscribe(new AnonymousObserver<MsgOp>(op => msgOpStreamRec = true));

            UnitUnderTest.Dispatch(PingOp.Instance);

            opStreamRec.Should().BeTrue();
            msgOpStreamRec.Should().BeFalse();
        }

        [Fact]
        public void Dispatching_non_MsgOp_Should_continue_dispatching_When_using_AnonymousObserver_with_error_handler_but_failing_observer_gets_discarded()
        {
            var countA = 0;
            var countB = 0;
            var countC = 0;
            var exToThrow = new Exception(Guid.NewGuid().ToString());
            Exception caughtEx = null;

            UnitUnderTest.AllOpsStream.Subscribe(new AnonymousObserver<IOp>(op =>
            {
                if (countA == 0)
                {
                    countA += 1;
                    throw exToThrow;
                }

                countA += 1;
            }, ex => caughtEx = ex));
            UnitUnderTest.AllOpsStream.Subscribe(new AnonymousObserver<IOp>(op => countB += 1));
            UnitUnderTest.AllOpsStream.Subscribe(new AnonymousObserver<IOp>(op => countC += 1));

            UnitUnderTest.Dispatch(PingOp.Instance);
            UnitUnderTest.Dispatch(PingOp.Instance);

            caughtEx.Should().Be(exToThrow);
            countA.Should().Be(1);
            countB.Should().Be(2);
            countC.Should().Be(2);
        }

        [Fact]
        public void Dispatching_non_MsgOp_Should_continue_dispatching_When_using_DelegatingObserver_with_error_handler_but_failing_observer_gets_discarded()
        {
            var countA = 0;
            var countB = 0;
            var countC = 0;
            var exToThrow = new Exception(Guid.NewGuid().ToString());
            Exception caughtEx = null;

            UnitUnderTest.AllOpsStream.Subscribe(new DelegatingObserver<IOp>(op =>
            {
                if (countA == 0)
                {
                    countA += 1;
                    throw exToThrow;
                }

                countA += 1;
            }, ex => caughtEx = ex));
            UnitUnderTest.AllOpsStream.Subscribe(new DelegatingObserver<IOp>(op => countB += 1));
            UnitUnderTest.AllOpsStream.Subscribe(new DelegatingObserver<IOp>(op => countC += 1));

            UnitUnderTest.Dispatch(PingOp.Instance);
            UnitUnderTest.Dispatch(PingOp.Instance);

            caughtEx.Should().Be(exToThrow);
            countA.Should().Be(1);
            countB.Should().Be(2);
            countC.Should().Be(2);
        }

        [Fact]
        public void Dispatching_non_MsgOp_Should_continue_dispatching_When_using_AnonymousObserver_without_error_handler_but_failing_observer_gets_discarded()
        {
            var countA = 0;
            var countB = 0;
            var countC = 0;

            UnitUnderTest.AllOpsStream.Subscribe(new AnonymousObserver<IOp>(op =>
            {
                if (countA == 0)
                {
                    countA += 1;
                    throw new Exception("Fail");
                }

                countA += 1;
            }));
            UnitUnderTest.AllOpsStream.Subscribe(new AnonymousObserver<IOp>(op => countB += 1));
            UnitUnderTest.AllOpsStream.Subscribe(new AnonymousObserver<IOp>(op => countC += 1));

            UnitUnderTest.Dispatch(PingOp.Instance);
            UnitUnderTest.Dispatch(PingOp.Instance);

            countA.Should().Be(1);
            countB.Should().Be(2);
            countC.Should().Be(2);
        }

        [Fact]
        public void Dispatching_non_MsgOp_Should_continue_dispatching_When_using_DelegatingObserver_without_error_handler_but_failing_observer_gets_discarded()
        {
            var countA = 0;
            var countB = 0;
            var countC = 0;

            UnitUnderTest.AllOpsStream.Subscribe(new DelegatingObserver<IOp>(op =>
            {
                if (countA == 0)
                {
                    countA += 1;
                    throw new Exception("Fail");
                }

                countA += 1;
            }));
            UnitUnderTest.AllOpsStream.Subscribe(new DelegatingObserver<IOp>(op => countB += 1));
            UnitUnderTest.AllOpsStream.Subscribe(new DelegatingObserver<IOp>(op => countC += 1));

            UnitUnderTest.Dispatch(PingOp.Instance);
            UnitUnderTest.Dispatch(PingOp.Instance);

            countA.Should().Be(1);
            countB.Should().Be(2);
            countC.Should().Be(2);
        }

        [Fact]
        public void Dispatching_MsgOp_Should_continue_dispatching_When_using_AnonymousObserver_with_error_handler_but_failing_observer_gets_discarded()
        {
            var msgOp = new MsgOp("TestSubject", "f0dd86b9c2804632919b7b78292435e6", new byte[0]);
            var countA = 0;
            var countB = 0;
            var countC = 0;
            var exToThrow = new Exception(Guid.NewGuid().ToString());
            Exception caughtEx = null;

            UnitUnderTest.MsgOpsStream.Subscribe(new AnonymousObserver<IOp>(op =>
            {
                if (countA == 0)
                {
                    countA += 1;
                    throw exToThrow;
                }

                countA += 1;
            }, ex => caughtEx = ex));
            UnitUnderTest.MsgOpsStream.Subscribe(new AnonymousObserver<IOp>(op => countB += 1));
            UnitUnderTest.MsgOpsStream.Subscribe(new AnonymousObserver<IOp>(op => countC += 1));

            UnitUnderTest.Dispatch(msgOp);
            UnitUnderTest.Dispatch(msgOp);

            caughtEx.Should().Be(exToThrow);
            countA.Should().Be(1);
            countB.Should().Be(2);
            countC.Should().Be(2);
        }

        [Fact]
        public void Dispatching_MsgOp_Should_continue_dispatching_When_using_DelegatingObserver_with_error_handler_but_failing_observer_gets_discarded()
        {
            var msgOp = new MsgOp("TestSubject", "01c549bed5f643e484c2841aff7a0d9d", new byte[0]);
            var countA = 0;
            var countB = 0;
            var countC = 0;
            var exToThrow = new Exception(Guid.NewGuid().ToString());
            Exception caughtEx = null;

            UnitUnderTest.MsgOpsStream.Subscribe(new DelegatingObserver<IOp>(op =>
            {
                if (countA == 0)
                {
                    countA += 1;
                    throw exToThrow;
                }

                countA += 1;
            }, ex => caughtEx = ex));
            UnitUnderTest.MsgOpsStream.Subscribe(new DelegatingObserver<IOp>(op => countB += 1));
            UnitUnderTest.MsgOpsStream.Subscribe(new DelegatingObserver<IOp>(op => countC += 1));

            UnitUnderTest.Dispatch(msgOp);
            UnitUnderTest.Dispatch(msgOp);

            caughtEx.Should().Be(exToThrow);
            countA.Should().Be(1);
            countB.Should().Be(2);
            countC.Should().Be(2);
        }

        [Fact]
        public void Dispatching_MsgOp_Should_continue_dispatching_When_using_AnonymousObserver_without_error_handler_but_failing_observer_gets_discarded()
        {
            var msgOp = new MsgOp("TestSubject", "60a152d4b5804b23abe088eeac63b55e", new byte[0]);
            var countA = 0;
            var countB = 0;
            var countC = 0;

            UnitUnderTest.MsgOpsStream.Subscribe(new AnonymousObserver<IOp>(op =>
            {
                if (countA == 0)
                {
                    countA += 1;
                    throw new Exception("Fail");
                }

                countA += 1;
            }));
            UnitUnderTest.MsgOpsStream.Subscribe(new AnonymousObserver<IOp>(op => countB += 1));
            UnitUnderTest.MsgOpsStream.Subscribe(new AnonymousObserver<IOp>(op => countC += 1));

            UnitUnderTest.Dispatch(msgOp);
            UnitUnderTest.Dispatch(msgOp);

            countA.Should().Be(1);
            countB.Should().Be(2);
            countC.Should().Be(2);
        }

        [Fact]
        public void Dispatching_MsgOp_Should_continue_dispatching_When_using_DelegatingObserver_without_error_handler_but_failing_observer_gets_discarded()
        {
            var msgOp = new MsgOp("TestSubject", "e8fb57beeb094bbfb545056057a8f7f2", new byte[0]);
            var countA = 0;
            var countB = 0;
            var countC = 0;

            UnitUnderTest.MsgOpsStream.Subscribe(new DelegatingObserver<IOp>(op =>
            {
                if (countA == 0)
                {
                    countA += 1;
                    throw new Exception("Fail");
                }

                countA += 1;
            }));
            UnitUnderTest.MsgOpsStream.Subscribe(new DelegatingObserver<IOp>(op => countB += 1));
            UnitUnderTest.MsgOpsStream.Subscribe(new DelegatingObserver<IOp>(op => countC += 1));

            UnitUnderTest.Dispatch(msgOp);
            UnitUnderTest.Dispatch(msgOp);

            countA.Should().Be(1);
            countB.Should().Be(2);
            countC.Should().Be(2);
        }
    }
}