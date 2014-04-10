/*
 * Original author: Nicholas Shulman <nicksh .at. u.washington.edu>,
 *                  MacCoss Lab, Department of Genome Sciences, UW
 *
 * Copyright 2009 University of Washington - Seattle, WA
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;

namespace pwiz.Topograph.Data
{
    public class DbLock : DbEntity<DbLock>
    {
        public virtual byte[] InstanceIdBytes { get; set; }
        public virtual Guid? InstanceIdGuid
        {
            get
            {
                if (InstanceIdBytes == null)
                {
                    return null;
                }
                return new Guid(InstanceIdBytes);
            }
            set
            {
                InstanceIdBytes = value == null ? null : value.Value.ToByteArray();
            }
        }
        public virtual LockType LockType { get; set; }
        public virtual long? WorkspaceId { get; set; }
        public virtual long? PeptideAnalysisId { get; set; }
        public virtual long? MsDataFileId { get; set; }
    }
    // ReSharper disable InconsistentNaming
    public enum LockType
    {
        chromatograms,
        results,
    }
    // ReSharper restore InconsistentNaming
    public class InvalidLockException : ApplicationException
    {
        
    }
}