using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace LibGit2Sharp
{
    /// <summary>
    /// Defines extensions related to file history.
    /// Note: These could potentially be moved to the RepositoryExtensions class.
    /// </summary>
    public static class FileHistoryExtensions
    {
        /// <summary>
        /// Gets the history of the file specified by <code>relativePath</code>.
        /// </summary>
        /// <param name="repo">The repository.</param>
        /// <param name="relativePath">The file's relative path.</param>
        /// <returns>The file's history.</returns>
        public static FileHistory GetFileHistory(this Repository repo, string relativePath)
        {
            return new FileHistory(repo.Commits, relativePath);
        }

        /// <summary>
        /// Gets the history of the file specified by <code>relativePath</code>.
        /// </summary>
        /// <param name="repo">The repository.</param>
        /// <param name="queryFilter">The filter to be used in querying commits.</param>
        /// <param name="relativePath">The file's relative path.</param>
        /// <returns>The file's history.</returns>
        public static FileHistory GetFileHistory(this Repository repo, CommitFilter queryFilter, string relativePath)
        {
            return new FileHistory(repo.Commits, queryFilter, relativePath);
        }
    }

    /// <summary>
    /// Represents a file's history of relevant commits or blobs.
    /// </summary>
    public class FileHistory
    {
        private readonly IQueryableCommitLog commitLog;
        private readonly string relativePath;
        private readonly CommitFilter queryFilter;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileHistory"/> class.
        /// The commits will be enumerated according in reverse chronological order.
        /// </summary>
        /// <param name="commitLog">The commit log.</param>
        /// <param name="relativePath">The file's relative path.</param>
        internal FileHistory(IQueryableCommitLog commitLog, string relativePath)
            : this(commitLog, new CommitFilter(), relativePath)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileHistory"/> class.
        /// </summary>
        /// <param name="commitLog">The commit log.</param>
        /// <param name="queryFilter">The filter to be used in querying commits.</param>
        /// <param name="relativePath">The file's relative path.</param>
        internal FileHistory(IQueryableCommitLog commitLog, CommitFilter queryFilter, string relativePath)
        {
            if (commitLog == null)
                throw new ArgumentNullException("commitLog");
            if (relativePath == null)
                throw new ArgumentNullException("relativePath");
            if (queryFilter == null)
                throw new ArgumentNullException("queryFilter");

            this.commitLog = commitLog;
            this.relativePath = relativePath;
            this.queryFilter = GetCommitFilter(queryFilter);
        }

        #region Public Interface

        /// <summary>
        /// Produces the collection of <see cref="FileHistoryEntry"/> instances representing the file history.
        /// </summary>
        /// <returns>A collection of <see cref="FileHistoryEntry"/> instances.</returns>
        public IEnumerable<FileHistoryEntry> RelevantCommits()
        {
            return RelevantCommits(this.commitLog, this.queryFilter, this.relativePath);
        }
        
        /// <summary>
        /// Gets collection of changed <see cref="Blob"/> instances, ignoring commits
        /// with which a file was only renamed.
        /// </summary>
        /// <returns>The collection of changed <see cref="Blob"/> instances.</returns>
        public IEnumerable<Blob> RelevantBlobs()
        {
            List<Blob> blobHistory = new List<Blob>();
            Blob lastAddedBlob = null;

            foreach (FileHistoryEntry entry in RelevantCommits())
            {
                Blob blob = entry.Commit.Tree[entry.RelativePath].Target as Blob;
                if (blob != null && !blob.Equals(lastAddedBlob))
                {
                    blobHistory.Add(blob);
                    lastAddedBlob = blob;
                }
            }

            return blobHistory;
        }

        #endregion

        /// <summary>
        /// Creates a <see cref="CommitFilter"/> from a base filter, setting <see cref="CommitFilter.SortBy"/>
        /// to <see cref="CommitSortStrategies.Time"/> and using all other attributes of the base filter.
        /// </summary>
        /// <param name="baseFilter">The base filter.</param>
        /// <returns>A new instance of <see cref="CommitFilter"/>.</returns>
        private CommitFilter GetCommitFilter(CommitFilter baseFilter)
        {
            return new CommitFilter
            {
                SortBy = CommitSortStrategies.Time,
                FirstParentOnly = baseFilter.FirstParentOnly,
                Since = baseFilter.Since,
                Until = baseFilter.Until
            };
        }

        /// <summary>
        /// Creates a <see cref="CommitFilter"/> from a base filter, setting <see cref="CommitFilter.SortBy"/>
        /// to <see cref="CommitSortStrategies.Time"/> and <see cref="CommitFilter.Since"/> to the given commit 
        /// while retaining all other base filter attribute values. 
        /// </summary>
        /// <param name="baseFilter">The base filter.</param>
        /// <param name="since">The first <see cref="Commit"/>.</param>
        /// <returns>A new instance of <see cref="CommitFilter"/>.</returns>
        private CommitFilter GetCommitFilter(CommitFilter baseFilter, Commit since)
        {
            CommitFilter filter = GetCommitFilter(baseFilter);
            filter.Since = since;
            return filter;
        }

        /// <summary>
        /// Produces the collection of <see cref="FileHistoryEntry"/> instances representing the history of the
        /// file specified by <code>relativePath</code>. Uses the given <see cref="CommitFilter"/> instance to
        /// filter the commit log.
        /// </summary>
        /// <param name="commitLog">The commit log.</param>
        /// <param name="filter">The <see cref="CommitFilter"/> to be used in querying commits.</param>
        /// <param name="relativePath">The file's relative path.</param>
        /// <returns>A list of <see cref="FileHistoryEntry"/> instances.</returns>
        private IEnumerable<FileHistoryEntry> RelevantCommits(IQueryableCommitLog commitLog, CommitFilter filter, string relativePath)
        {
            List<FileHistoryEntry> relevantCommits = new List<FileHistoryEntry>();

            // Get all commits containing changes to the named file.
            List<Commit> commitRange = GetCommits(commitLog, filter, relativePath);
            if (commitRange.Count() > 0)
            {
                relevantCommits.AddRange(commitRange.Select(c => new FileHistoryEntry(relativePath, c)));

                // See whether the file was renamed. Append the next commit ranges as necessary.
                string lastSha = commitRange.Last().Tree[relativePath].Target.Sha;
                if (lastSha != null)
                {
                    CommitFilter endOfPreviousRangeFilter = GetCommitFilter(filter, commitRange.Last());
                    Commit nextCommit = commitLog.QueryBy(endOfPreviousRangeFilter).Skip(1)
                        .FirstOrDefault(c => GetFirstRelativePath(c.Tree, lastSha) != null);
                    if (nextCommit != null)
                    {
                        string previousPath = GetFirstRelativePath(nextCommit.Tree, lastSha);
                        CommitFilter startOfNextRangeFilter = GetCommitFilter(filter, nextCommit);
                        relevantCommits.AddRange(RelevantCommits(commitLog, startOfNextRangeFilter, previousPath));
                    }
                }
            }

            return relevantCommits;
        }

        /// <summary>
        /// Produces a list of <see cref="Commit"/> instances, each representing a relevant change of
        /// the file specified by <code>relativePath</code>. Uses the given <see cref="CommitFilter"/> 
        /// instance to filter the commit log.
        /// </summary>
        /// <param name="commitLog">The commit log.</param>
        /// <param name="filter">The <see cref="CommitFilter"/> to be used in querying commits.</param>
        /// <param name="relativePath">The file's relative path.</param>
        /// <returns>The list of <see cref="Commit"/> instances representing the named file's change history.</returns>
        private List<Commit> GetCommits(IQueryableCommitLog commitLog, CommitFilter filter, string relativePath)
        {
            Func<Commit, bool> isRootCommit = c => c.Parents.Count() == 0;
            Func<Commit, bool> isMergeCommit = c => c.Parents.Count() > 1;
            Func<Commit, bool> isFileNewOrChanged = c => c.Parents.All(
                p => p.Tree[relativePath] == null ||
                     p.Tree[relativePath].Target.Id != c.Tree[relativePath].Target.Id);

            return commitLog.QueryBy(filter)
                .TakeWhile(c => c.Tree[relativePath] != null)
                .Where(c => isRootCommit(c) || (!isMergeCommit(c) && isFileNewOrChanged(c)))
                .ToList();
        }

        /// <summary>
        /// Gets the relative path of the first target having the given SHA.
        /// </summary>
        /// <param name="tree">The tree.</param>
        /// <param name="sha">The SHA.</param>
        /// <returns>The relative path of the first target having the given SHA.</returns>
        private string GetFirstRelativePath(Tree tree, string sha)
        {
            // See whether the given tree contains a target having the desired SHA.
            string path = tree.Where(e => e.Target.Sha == sha).Select(e => e.Path).FirstOrDefault();
            if (path != null)
                return path;

            // The target was not found. Thus, we'll have a look at the subtrees.
            return tree
                .Where(e => e.TargetType == TreeEntryTargetType.Tree)
                .Select(e => GetFirstRelativePath((Tree)e.Target, sha))
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// An entry in a file's commit history.
    /// </summary>
    public class FileHistoryEntry
    {
        /// <summary>
        /// Creates a new instance of the <see cref="FileHistoryEntry"/> class.
        /// </summary>
        /// <param name="relativePath">The relative file path.</param>
        /// <param name="commit">The commit in which the file was created, changed, or renamed.</param>
        internal FileHistoryEntry(string relativePath, Commit commit)
        {
            RelativePath = relativePath;
            Commit = commit;
        }

        /// <summary>
        /// The file's relative path.
        /// </summary>
        public string RelativePath { get; internal set; }

        /// <summary>
        /// The commit in which the file was created or changed.
        /// </summary>
        public Commit Commit { get; internal set; }
    }
}
