//
// Reader_Bruker_Detail.cpp
//
// 
// Original author: Matt Chambers <matt.chambers .@. vanderbilt.edu>
//
// Copyright 2008 Vanderbilt University - Nashville, TN 37232
//
// Licensed under Creative Commons 3.0 United States License, which requires:
//  - Attribution
//  - Noncommercial
//  - No Derivative Works
//
// http://creativecommons.org/licenses/by-nc-nd/3.0/us/
//
// Unless required by applicable law or agreed to in writing, software 
// distributed under the License is distributed on an "AS IS" BASIS, 
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
// See the License for the specific language governing permissions and 
// limitations under the License.
//


#include "Reader_Bruker_Detail.hpp"
#include "pwiz/utility/misc/String.hpp"
#include "pwiz/utility/misc/Filesystem.hpp"


namespace pwiz {
namespace msdata {
namespace detail {

using namespace std;

SpectrumList_Bruker_Format format(const string& path)
{
    bfs::path sourcePath(path);

    // Make sure target "path" is actually a directory since
    // all Bruker formats are directory-based
    if (!bfs::is_directory(sourcePath))
    {
        // Special cases for identifying direct paths to fid/Analysis.yep/Analysis.baf
        std::string leaf = sourcePath.leaf();
        if (leaf == "fid" && !bfs::exists(sourcePath.branch_path() / "Analysis.baf"))
            return SpectrumList_Bruker_Format_FID;
        else if(leaf == "Analysis.yep")
            return SpectrumList_Bruker_Format_YEP;
        else if(leaf == "Analysis.baf")
            return SpectrumList_Bruker_Format_BAF;
        else
            return SpectrumList_Bruker_Format_Unknown;
    }

    // TODO: 1SRef is not the only possible substring below, get more examples!

    // Check for fid-based data;
    // Every directory within the queried directory should have a "1/1SRef"
    // subdirectory with a fid file in it, but we check only the first non-dotted
    // directory for efficiency. This can fail, but those failures are acceptable.
    // Alternatively, a directory closer to the fid file can be identified.
    // Caveat: BAF files may be accompanied by a fid, skip these cases! (?)
    const static bfs::directory_iterator endItr;
    bfs::directory_iterator itr(sourcePath);
    for (; itr != endItr; ++itr)
        if (itr->path().leaf()[0] == '.')
            continue;
        else if (bfs::exists(itr->path() / "1/1SRef/fid") ||
                 bfs::exists(itr->path() / "1SRef/fid") ||
                 (bfs::exists(itr->path() / "fid") && !bfs::exists(itr->path() / "Analysis.baf")) ||
                 (bfs::exists(sourcePath / "fid") && !bfs::exists(sourcePath / "Analysis.baf")))
                return SpectrumList_Bruker_Format_FID;
        else
            break;

    // Check for yep-based data;
    // The directory should have a file named "Analysis.yep"
    if (bfs::exists(sourcePath / "Analysis.yep"))
        return SpectrumList_Bruker_Format_YEP;

    // Check for baf-based data;
    // The directory should have a file named "Analysis.baf"
    if (bfs::exists(sourcePath / "Analysis.baf"))
        return SpectrumList_Bruker_Format_BAF;

    return SpectrumList_Bruker_Format_Unknown;
}


} // namespace detail
} // namespace msdata
} // namespace pwiz
