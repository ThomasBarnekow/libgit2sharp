using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibGit2Sharp.Tests.TestHelpers;
using Xunit;
using Xunit.Extensions;

namespace LibGit2Sharp.Tests
{
    public class FileHistoryFixture : BaseFixture
    {
        [Fact]
        public void EmptyRepositoryHasNoHistory()
        {
            string repoPath = CreateEmptyRepository();
            using (Repository repo = new Repository(repoPath))
            {
                FileHistory history = repo.GetFileHistory("Test.txt");
                Assert.Equal(0, history.RelevantCommits().Count());
                Assert.Equal(0, history.RelevantBlobs().Count());
            }
        }

        [Fact]
        public void CanTellSingleCommitHistory()
        {
            string repoPath = CreateEmptyRepository();
            using (Repository repo = new Repository(repoPath))
            {
                // Set up repository.
                string relativePath = "Test.txt";
                Commit commit = MakeAndCommitChange(repo, repoPath, relativePath, "Hello World");

                // Perform tests.
                FileHistory history = repo.GetFileHistory(relativePath);
                IEnumerable<FileHistoryEntry> relevantCommits = history.RelevantCommits();
                IEnumerable<Blob> relevantBlobs = history.RelevantBlobs();

                Assert.Equal(1, relevantCommits.Count());
                Assert.Equal(1, relevantBlobs.Count());

                Assert.Equal(relativePath, relevantCommits.First().RelativePath);
                Assert.Equal(commit, relevantCommits.First().Commit);
            }
        }

        [Fact]
        public void CanTellSimpleCommitHistory()
        {
            string relativePath1 = "Test1.txt";
            string relativePath2 = "Test2.txt";

            string repoPath = CreateEmptyRepository();
            using (Repository repo = new Repository(repoPath))
            {
                // Set up repository.
                Commit commit1 = MakeAndCommitChange(repo, repoPath, relativePath1, "Hello World");
                Commit commit2 = MakeAndCommitChange(repo, repoPath, relativePath2, "Second file's contents");
                Commit commit3 = MakeAndCommitChange(repo, repoPath, relativePath1, "Hello World again");

                // Perform tests.
                FileHistory history = repo.GetFileHistory(relativePath1);
                IEnumerable<FileHistoryEntry> relevantCommits = history.RelevantCommits();
                IEnumerable<Blob> relevantBlobs = history.RelevantBlobs();

                Assert.Equal(2, relevantCommits.Count());
                Assert.Equal(2, relevantBlobs.Count());

                Assert.Equal(commit3, relevantCommits.ElementAt(0).Commit);
                Assert.Equal(commit1, relevantCommits.ElementAt(1).Commit);
            }
        }

        [Fact]
        public void CanTellComplexCommitHistory()
        {
            string relativePath1 = "Test1.txt";
            string relativePath2 = "Test2.txt";

            // Create an empty repository with one subfolder.
            string repoPath = CreateEmptyRepository();
            using (Repository repo = new Repository(repoPath))
            {
                // Make initial changes.
                Commit commit1 = MakeAndCommitChange(repo, repoPath, relativePath1, "Hello World");
                MakeAndCommitChange(repo, repoPath, relativePath2, "Second file's contents");
                Commit commit2 = MakeAndCommitChange(repo, repoPath, relativePath1, "Hello World again");

                // Move the first file to a new directory.
                string newRelativePath1 = Path.Combine(relativeSubFolderPath1, relativePath1);
                repo.Move(relativePath1, newRelativePath1);
                Commit commit3 = repo.Commit("Moved " + relativePath1 + " to " + newRelativePath1, CreateSignature());

                // Make further changes.
                MakeAndCommitChange(repo, repoPath, relativePath2, "Changed second file's contents");
                Commit commit4 = MakeAndCommitChange(repo, repoPath, newRelativePath1, "I have done it again!");

                // Perform tests.
                FileHistory history = repo.GetFileHistory(newRelativePath1);
                IEnumerable<FileHistoryEntry> relevantCommits = history.RelevantCommits();
                IEnumerable<Blob> relevantBlobs = history.RelevantBlobs();

                Assert.Equal(4, relevantCommits.Count());
                Assert.Equal(3, relevantBlobs.Count());

                Assert.Equal(2, relevantCommits.Where(e => e.RelativePath == newRelativePath1).Count());
                Assert.Equal(2, relevantCommits.Where(e => e.RelativePath == relativePath1).Count());

                Assert.Equal(commit4, relevantCommits.ElementAt(0).Commit);
                Assert.Equal(commit3, relevantCommits.ElementAt(1).Commit);
                Assert.Equal(commit2, relevantCommits.ElementAt(2).Commit);
                Assert.Equal(commit1, relevantCommits.ElementAt(3).Commit);

                Assert.Equal(commit4.Tree[newRelativePath1].Target, relevantBlobs.ElementAt(0));
                Assert.Equal(commit2.Tree[relativePath1].Target, relevantBlobs.ElementAt(1));
                Assert.Equal(commit1.Tree[relativePath1].Target, relevantBlobs.ElementAt(2));
            }
        }

        #region Helpers

        protected string relativeSubFolderPath1 = "SubFolder1";
        protected string relativeSubFolderPath2 = "SubFolder2";

        protected string CreateEmptyRepository()
        {
            // Create a new empty directory.
            SelfCleaningDirectory scd = BuildSelfCleaningDirectory();

            // Initialize a GIT repository in that directory.
            Repository.Init(scd.DirectoryPath, false);

            // Create subfolders in that directory.
            Directory.CreateDirectory(Path.Combine(scd.DirectoryPath, relativeSubFolderPath1));
            Directory.CreateDirectory(Path.Combine(scd.DirectoryPath, relativeSubFolderPath2));

            // Done.
            return scd.DirectoryPath;
        }

        protected Commit MakeAndCommitChange(Repository repo, string repoPath, string relativePath, string text)
        {
            CreateFile(repoPath, relativePath, text);
            repo.Index.Add(relativePath);
            return repo.Commit("Changed " + relativePath, CreateSignature());
        }
        
        protected string CreateFile(string repoPath, string relativePath, string text)
        {
            string absolutePath = Path.Combine(repoPath, relativePath);
            using (StreamWriter sw = File.CreateText(absolutePath))
                sw.WriteLine(text);

            return absolutePath;
        }

        protected Signature CreateSignature()
        {
            return new Signature("tester", "tester@email.com", DateTimeOffset.Now);
        }

        #endregion
    }
}
