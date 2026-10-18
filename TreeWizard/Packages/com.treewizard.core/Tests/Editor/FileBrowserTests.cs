using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TreeWizard.UI;
using UnityEngine.UIElements;

namespace TreeWizard.Editor.Tests
{
    public class FileBrowserTests
    {
        string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "TreeWizardFileBrowserTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "Bark"));
            Directory.CreateDirectory(Path.Combine(_root, "leaves"));
            Directory.CreateDirectory(Path.Combine(_root, ".git"));
            File.WriteAllText(Path.Combine(_root, "tree10.treewizard"), "{}");
            File.WriteAllText(Path.Combine(_root, "tree2.treewizard"), "{}");
            File.WriteAllText(Path.Combine(_root, "Oak.FBX"), new string('x', 2000));
            File.WriteAllText(Path.Combine(_root, "notes.txt"), "");
            File.WriteAllText(Path.Combine(_root, ".DS_Store"), "");
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_root, true);

        [Test]
        public void Filter_MatchesItsExtensionsInAnyCase()
        {
            var images = new FileFilter("Images", "png", ".JPG");

            CollectionAssert.AreEqual(new[] { ".png", ".jpg" }, images.Extensions);
            Assert.IsTrue(images.Matches("leaf.PNG"));
            Assert.IsTrue(images.Matches("bark.jpg"));
            Assert.IsFalse(images.Matches("tree.fbx"));
            Assert.IsTrue(FileFilter.All.Matches("anything.at.all"));
            Assert.AreEqual("Images (*.png, *.jpg)", images.ToString());
        }

        [Test]
        public void List_PutsFoldersFirst_SkipsHidden_AndFiltersFiles()
        {
            var entries = FolderListing.List(_root, new FileFilter("Designs", ".treewizard"));

            CollectionAssert.AreEqual(new[] { "Bark", "leaves", "tree2.treewizard", "tree10.treewizard" }, entries.Select(e => e.Name));
            Assert.IsTrue(entries[0].IsFolder);
            Assert.AreEqual(-1, entries[0].Size);
        }

        [Test]
        public void List_CanLeaveOutFiles()
        {
            var entries = FolderListing.List(_root, FileFilter.All, includeFiles: false);

            Assert.IsTrue(entries.All(e => e.IsFolder));
            Assert.AreEqual(2, entries.Count);
        }

        [Test]
        public void Sort_BySizeDescending_KeepsFoldersFirst()
        {
            var entries = FolderListing.List(_root, FileFilter.All);
            FolderListing.Sort(entries, FileSortColumn.Size, false);

            Assert.IsTrue(entries.Take(2).All(e => e.IsFolder));
            Assert.AreEqual("Oak.FBX", entries[2].Name);
        }

        [TestCase("tree2", "tree10", -1)]
        [TestCase("Tree", "tree", 0)]
        [TestCase("a01", "a1", 0)]
        [TestCase("b", "a10", 1)]
        public void NaturalCompare_ComparesDigitsAsNumbers(string a, string b, int expected)
        {
            Assert.AreEqual(expected, Math.Sign(FolderListing.NaturalCompare(a, b)));
        }

        [TestCase("Oak tree.fbx", true)]
        [TestCase("", false)]
        [TestCase("a/b", false)]
        [TestCase("what?", false)]
        [TestCase("con.txt", false)]
        [TestCase("trailing.", false)]
        [TestCase("..", false)]
        public void ValidateName_RejectsNamesSomeOsCantStore(string name, bool valid)
        {
            Assert.AreEqual(valid, FolderListing.ValidateName(name) == null);
        }

        [Test]
        public void Resolve_HandlesRelativeAbsoluteAndHome()
        {
            Assert.AreEqual(Path.Combine(_root, "Bark"), FolderListing.Resolve(_root, "Bark"));
            Assert.AreEqual(Path.GetFullPath(_root), FolderListing.Resolve(Path.Combine(_root, "Bark"), ".."));
            Assert.AreEqual(Path.Combine(_root, "leaves"), FolderListing.Resolve("/somewhere/else", Path.Combine(_root, "leaves")));
            Assert.AreEqual(Path.GetFullPath(Places.Home), FolderListing.Resolve(_root, "~"));
        }

        [Test]
        public void Breadcrumbs_RunFromTheRootToTheFolder()
        {
            var folder = Path.Combine(_root, "Bark");
            var crumbs = FolderListing.Breadcrumbs(folder);

            Assert.AreEqual(Path.GetPathRoot(folder), crumbs[0].path);
            Assert.AreEqual(("Bark", Path.GetFullPath(folder)), crumbs[crumbs.Count - 1]);
        }

        [Test]
        public void UniqueName_CountsUpPastExistingNames()
        {
            Assert.AreEqual("Moss", FolderListing.UniqueName(_root, "Moss"));
            Assert.AreEqual("Bark 2", FolderListing.UniqueName(_root, "Bark"));
        }

        [TestCase(840, "840 bytes")]
        [TestCase(12_400, "12 KB")]
        [TestCase(3_400_000, "3.4 MB")]
        public void FormatSize_IsReadable(long bytes, string expected)
        {
            Assert.AreEqual(expected, FolderListing.FormatSize(bytes));
        }

        [Test]
        public void History_GoesBackAndForward_AndForgetsTheFutureOnANewVisit()
        {
            var history = new NavigationHistory();
            history.Visit("a");
            history.Visit("b");
            history.Visit("c");

            Assert.AreEqual("b", history.Back());
            Assert.AreEqual("a", history.Back());
            Assert.IsFalse(history.CanGoBack);
            Assert.AreEqual("b", history.Forward());

            history.Visit("d");
            Assert.IsFalse(history.CanGoForward);
            Assert.AreEqual("b", history.Back());
        }

        [Test]
        public void Places_IncludeHome_AndUseRealDocumentsFolder()
        {
            var places = Places.Standard();

            Assert.AreEqual(PlaceKind.Home, places[0].Kind);
            Assert.That(places.Where(p => p.Kind == PlaceKind.Documents).Select(p => p.Path),
                Is.All.Not.EqualTo(Places.Home), "Mono reports the home folder as Documents on macOS and Linux");
        }

        [Test]
        public void Browser_SavesWithTheFiltersExtension()
        {
            var browser = new FileBrowser();
            string saved = null;
            browser.SaveFile("Save", _root, "Spruce", new[] { new FileFilter("Designs", ".treewizard") }, path => saved = path);

            Assert.IsTrue(browser.IsOpen);
            Assert.AreEqual(4, browser.Entries.Count, "two folders and two designs");

            browser.Confirm();

            Assert.AreEqual(Path.Combine(Path.GetFullPath(_root), "Spruce.treewizard"), saved);
            Assert.IsFalse(browser.IsOpen);
            Assert.AreEqual(Path.GetFullPath(_root), browser.RecentFolders[0]);
        }

        [Test]
        public void Browser_AsksBeforeReplacingAFile()
        {
            var browser = new FileBrowser();
            string saved = null;
            browser.SaveFile("Save", _root, "tree2", new[] { new FileFilter("Designs", ".treewizard") }, path => saved = path);

            browser.Confirm();

            Assert.IsNull(saved, "waits for the replace prompt");
            Assert.IsTrue(browser.IsOpen);
            Assert.IsNotNull(browser.Q<Label>(className: "tw-file-browser__prompt-text").text);
        }

        [Test]
        public void Browser_StartsInTheNearestFolderThatExists()
        {
            var browser = new FileBrowser();
            browser.SelectFolder("Pick", Path.Combine(_root, "missing", "deeper"), _ => { });

            Assert.AreEqual(Path.GetFullPath(_root), browser.CurrentFolder);
            Assert.IsTrue(browser.Entries.All(e => e.IsFolder), "folder mode lists only folders");
        }

        [Test]
        public void Browser_KeepsOpenAndShowsTheErrorWhenTheHostFails()
        {
            var browser = new FileBrowser();
            browser.SaveFile("Save", _root, "x", null, _ => throw new IOException("Disk full"));

            browser.Confirm();

            Assert.IsTrue(browser.IsOpen);
            Assert.AreEqual("Disk full", browser.Q<Label>(className: "tw-file-browser__message").text);
        }
    }
}
