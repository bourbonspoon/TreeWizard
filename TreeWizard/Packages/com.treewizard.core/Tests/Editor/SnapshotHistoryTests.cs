using NUnit.Framework;
using TreeWizard.UI;

namespace TreeWizard.Editor.Tests
{
    public class SnapshotHistoryTests
    {
        [Test]
        public void UndoAndRedo_StepThroughChanges()
        {
            var history = new SnapshotHistory();
            history.Reset("a");
            history.Record("b", 0f);
            history.Record("c", 10f);

            Assert.AreEqual("b", history.Undo());
            Assert.AreEqual("a", history.Undo());
            Assert.IsNull(history.Undo());
            Assert.AreEqual("b", history.Redo());
            Assert.AreEqual("c", history.Redo());
            Assert.IsNull(history.Redo());
        }

        [Test]
        public void QuickMergeableChanges_AreOneStep()
        {
            var history = new SnapshotHistory(mergeSeconds: 0.5f);
            history.Reset("0");
            history.Record("1", 0f, true);
            history.Record("2", 0.2f, true);
            history.Record("3", 0.4f, true); // a slider drag
            history.Record("4", 5f, true);   // a later drag

            Assert.AreEqual("3", history.Undo());
            Assert.AreEqual("0", history.Undo());
            Assert.IsFalse(history.CanUndo);
        }

        [Test]
        public void DiscreteChanges_NeverMerge()
        {
            var history = new SnapshotHistory();
            history.Reset("a");
            history.Record("b", 0f, true);
            history.Record("c", 0.1f); // a button press right after the drag

            Assert.AreEqual("b", history.Undo());
        }

        [Test]
        public void ANewChange_ClearsRedo_AndUnchangedStatesAreIgnored()
        {
            var history = new SnapshotHistory();
            history.Reset("a");
            history.Record("b", 0f);
            history.Undo();
            history.Record("a", 1f);
            Assert.IsTrue(history.CanRedo, "same state: nothing recorded");

            history.Record("c", 2f);
            Assert.IsFalse(history.CanRedo);
            Assert.AreEqual("a", history.Undo());
        }

        [Test]
        public void OldestSteps_AreDroppedPastTheLimit()
        {
            var history = new SnapshotHistory(limit: 2);
            history.Reset("a");
            history.Record("b", 0f);
            history.Record("c", 10f);
            history.Record("d", 20f);

            Assert.AreEqual("c", history.Undo());
            Assert.AreEqual("b", history.Undo());
            Assert.IsFalse(history.CanUndo);
        }
    }
}
