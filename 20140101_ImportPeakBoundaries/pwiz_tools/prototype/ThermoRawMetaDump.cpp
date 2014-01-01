//
// $Id$
//
//
// Original author: Matt Chambers <matt.chambers .@. vanderbilt.edu>
//
// Copyright 2013 Vanderbilt University - Nashville, TN 37232
//
// Licensed under the Apache License, Version 2.0 (the "License"); 
// you may not use this file except in compliance with the License. 
// You may obtain a copy of the License at 
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software 
// distributed under the License is distributed on an "AS IS" BASIS, 
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
// See the License for the specific language governing permissions and 
// limitations under the License.
//


#include "pwiz_aux/msrc/utility/vendor_api/thermo/RawFile.h"
#include "pwiz/utility/misc/Std.hpp"
#include "pwiz/utility/misc/Filesystem.hpp"


using namespace pwiz::util;
namespace thermo = pwiz::vendor_api::Thermo;


int main(int argc, const char* argv[])
{
    if (argc < 2)
    {
        cerr << "ThermoRawMetaDump extracts methods, tunes, and headers from RAW files.\n";
        cerr << "Usage: ThermoRawMetaDump <RAW filemask> <another RAW filemask>" << endl;
        return 1;
    }

    try
    {
        vector<string> filemasks(argv+1, argv+argc);

        vector<bfs::path> filenames;
        BOOST_FOREACH(const string& filemask, filemasks)
        {
            expand_pathmask(bfs::path(filemask), filenames);
            if (!filenames.size())
                throw runtime_error("no files found matching filemask \"" + filemask + "\"");
        }

        BOOST_FOREACH(const bfs::path& filename, filenames)
        {
            try
            {
                thermo::RawFilePtr rawfile = thermo::RawFile::create(filename.string());

                cout << " ==== Instrument methods for " << filename.filename() << " ====" << endl;
                auto_ptr<thermo::LabelValueArray> instrumentMethods = rawfile->getInstrumentMethods();
                for (int i=0; i < instrumentMethods->size(); ++i)
                    cout << instrumentMethods->label(i) << ":\n" << instrumentMethods->value(i) << endl << endl;
                cout << " ==== " << endl << endl;
                
                rawfile->setCurrentController(thermo::Controller_MS, 1);

                // loop until there are no more segments
                for(int segment=0;; ++segment)
                {
                    try
                    {
                        auto_ptr<thermo::LabelValueArray> tuneData = rawfile->getTuneData(segment);
                        cout << " ==== Tune data for " << filename.filename() << " segment " << (segment+1) << " ====" << endl;
                        for (int i=0; i < tuneData->size(); ++i)
                            cout << tuneData->label(i) << " " << tuneData->value(i) << "\n";
                        cout << " ==== " << endl << endl;
                    } catch(thermo::RawEgg&)
                    {
                        break;
                    }
                }

                cout << " ==== Sample/file/header information for " << filename.filename() << " ====" << endl;
                for (int i=0; i < (int) thermo::ValueID_Double_Count; ++i)
                    if (rawfile->value((thermo::ValueID_Double) i) > 0)
                        cout << rawfile->name((thermo::ValueID_Double) i) << ": " << lexical_cast<string>(rawfile->value((thermo::ValueID_Double) i)) << "\n";
                for (int i=0; i < (int) thermo::ValueID_Long_Count; ++i)
                    if (rawfile->value((thermo::ValueID_Long) i) > 0)
                        cout << rawfile->name((thermo::ValueID_Long) i) << ": " << lexical_cast<string>(rawfile->value((thermo::ValueID_Long) i)) << "\n";
                for (int i=0; i < (int) thermo::ValueID_String_Count; ++i)
                    if (!rawfile->value((thermo::ValueID_String) i).empty())
                        cout << rawfile->name((thermo::ValueID_String) i) << ": " << rawfile->value((thermo::ValueID_String) i) << "\n";
                cout << " ==== " << endl << endl;
            }
            catch (exception& e)
            {
                cerr << "Error: " << e.what() << endl;
            }
            catch (...)
            {
                cerr << "Unknown exception." << endl;
            }
        }
        return 0;
    }
    catch (exception& e)
    {
        cerr << "Error: " << e.what() << endl;
    }
    catch (...)
    {
        cerr << "Unknown exception." << endl;
    }

    return 1;
}
