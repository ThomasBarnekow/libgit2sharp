using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using LibGit2Sharp.Core;

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
        /// <param name="path">The file's path relative to the repository's root.</param>
        /// <returns>The file's history.</returns>
        public static FileHistory GetFileHistory(this Repository repo, string path)
        {
            return new FileHistory(repo, path);
        }

        /// <summary>
        /// Gets the history of the file specified by <code>relativePath</code>.
        /// </summary>
        /// <param name="repo">The repository.</param>
        /// <param name="queryFilter">The filter to be used in querying commits.</param>
        /// <param name="path">The file's path relative to the repository's root.</param>
        /// <returns>The file's history.</returns>
        public static FileHistory GetFileHistory(this Repository repo, CommitFilter queryFilter, string path)
        {
            return new FileHistory(repo, queryFilter, path);
        }
    }

    /// <summary>
    /// Represents a file's history of relevant commits or blobs.
    /// </summary>
    public class FileHistory
    {
        private readonly Repository repo;
        private readonly CommitFilter queryFilter;
        private readonly string path;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileHistory"/> class.
        /// </summary>
        /// <remarks>
        /// This constructor is required in testing contexts.
        /// </remarks>
        protected FileHistory()
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileHistory"/> class.
        /// The commits will be enumerated according in reverse chronological order.
        /// </summary>
        /// <param name="repo">The repository.</param>
        /// <param name="path">The file's path relative to the repository's root.</param>
        internal FileHistory(Repository repo, string path)
            : this(repo, new CommitFilter(), path)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileHistory"/> class.
        /// </summary>
        /// <param name="repo">The repository.</param>
        /// <param name="queryFilter">The filter to be used in querying commits.</param>
        /// <param name="path">The file's path relative to the repository's root.</param>
        internal FileHistory(Repository repo, CommitFilter queryFilter, string path)
        {
            Ensure.ArgumentNotNull(repo, "repo");
            Ensure.ArgumentNotNull(queryFilter, "queryFilter");
            Ensure.ArgumentNotNull(path, "path");

            this.repo = repo;
            this.queryFilter = GetCommitFilter(queryFilter);
            this.path = path;
        }

        #region Public Interface

        /// <summary>
        /// Produces the collection of <see cref="FileHistoryEntry"/> instances representing the file history.
        /// </summary>
        /// <returns>A collection of <see cref="FileHistoryEntry"/> instances.</returns>
        public virtual IEnumerable<FileHistoryEntry> RelevantCommits()
        {
            return RelevantCommits(this.repo, this.queryFilter, this.path);
        }

        /// <summary>
        /// Gets collection of changed <see cref="Blob"/> instances, ignoring commits
        /// with which a file was only renamed.
        /// </summary>
        /// <returns>The collection of changed <see cref="Blob"/> instances.</returns>
        public virtual IEnumerable<Blob> RelevantBlobs()
        {
            List<Blob> blobHistory = new List<Blob>();
            Blob lastAddedBlob = null;

            foreach (FileHistoryEntry entry in RelevantCommits())
            {
                Blob blob = entry.Commit.Tree[entry.Path].Target as Blob;
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
        /// The default commit sort strategy used.
        /// </summary>
        private static readonly CommitSortStrategies DefaultSortStrategy = CommitSortStrategies.Time;

        /// <summary>
        /// The allowed commit sort strategies.
        /// </summary>
        private static readonly List<CommitSortStrategies> AllowedSortStrategies = new List<CommitSortStrategies>
        {
            CommitSortStrategies.Topological, CommitSortStrategies.Time
        };

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
                SortBy = AllowedSortStrategies.Contains(baseFilter.SortBy) ? baseFilter.SortBy : DefaultSortStrategy,
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
        /// Gets the relevant commits in which the given file was created, changed, or renamed.
        /// </summary>
        /// <param name="repo">The repository.</param>
        /// <param name="filter">The filter to be used in querying commits.</param>
        /// <param name="path">The file's path relative to the repository's root.</param>
        /// <returns>A collection of <see cref="FileHistoryEntry"/> instances.</returns>
        protected virtual IEnumerable<FileHistoryEntry> RelevantCommits(Repository repo, CommitFilter filter, string path)
        {
            List<FileHistoryEntry> relevantCommits = new List<FileHistoryEntry>();

            // Get all commits containing changes to the named file.
            List<Commit> commitRange = GetCommits(repo.Commits.QueryBy(filter), path);
            if (commitRange.Count() > 0)
            {
                relevantCommits.AddRange(commitRange.Select(c => new FileHistoryEntry(path, c)));

                // See whether the file was renamed. Append the next commit ranges as necessary.
                Commit lastCommit = commitRange.Last();
                Commit parentCommit = lastCommit.Parents.SingleOrDefault();
                if (parentCommit != null)
                {
                    TreeChanges treeChanges = repo.Diff.Compare<TreeChanges>(parentCommit.Tree, lastCommit.Tree);
                    TreeEntryChanges treeEntryChanges = treeChanges[path];
                    if (treeEntryChanges != null && treeEntryChanges.Status == ChangeKind.Renamed)
                    {
                        CommitFilter parentFilter = GetCommitFilter(filter, parentCommit);
                        string parentPath = treeEntryChanges.OldPath;
                        relevantCommits.AddRange(RelevantCommits(repo, parentFilter, parentPath));
                    }
                }
            }

            return relevantCommits;
        }

        /// <summary>
        /// Produces a list of <see cref="Commit"/> instances, each representing a relevant change of
        /// the file specified by <code>relativePath</code>.
        /// </summary>
        /// <param name="commitLog">The commit log.</param>
        /// <param name="path">The file's path relative to the repository's root.</param>
        /// <returns>The list of <see cref="Commit"/> instances representing the named file's change history.</returns>
        protected virtual List<Commit> GetCommits(ICommitLog commitLog, string path)
        {
            Func<Commit, bool> isRootCommit = c => c.Parents.Count() == 0;
            Func<Commit, bool> isMergeCommit = c => c.Parents.Count() > 1;
            Func<Commit, bool> isFileNewOrChanged = c => c.Parents.All(
                p => p.Tree[path] == null ||
                     p.Tree[path].Target.Id != c.Tree[path].Target.Id);

            return commitLog
                .TakeWhile(c => c.Tree[path] != null)
                .Where(c => isRootCommit(c) || (!isMergeCommit(c) && isFileNewOrChanged(c)))
                .ToList();
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
        /// <remarks>
        /// This constructor is required in testing contexts.
        /// </remarks>
        protected FileHistoryEntry()
        { }

        /// <summary>
        /// Creates a new instance of the <see cref="FileHistoryEntry"/> class.
        /// </summary>
        /// <param name="path">The file's path.</param>
        /// <param name="commit">The commit in which the file was created, changed, or renamed.</param>
        internal FileHistoryEntry(string path, Commit commit)
        {
            Path = path;
            Commit = commit;
        }

        /// <summary>
        /// The file's path relative to the repository's root.
        /// </summary>
        public virtual string Path { get; internal set; }

        /// <summary>
        /// The commit in which the file was created or changed.
        /// </summary>
        public virtual Commit Commit { get; internal set; }
    }
}
