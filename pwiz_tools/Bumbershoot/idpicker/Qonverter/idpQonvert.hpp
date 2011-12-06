//
// $Id$
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
// The Original Code is the IDPicker suite.
//
// The Initial Developer of the Original Code is Matt Chambers.
//
// Copyright 2009 Vanderbilt University
//
// Contributor(s): Surendra Dasaris
//

#ifndef _IDPQONVERT_H
#define _IDPQONVERT_H

#include "BaseRunTimeConfig.h"
#include "Qonverter.hpp"
#include <iostream>
#include <sstream>
#include <string>

#define IDPQONVERT_RUNTIME_CONFIG \
	RTCONFIG_VARIABLE( string,			OutputSuffix,				""				) \
	RTCONFIG_VARIABLE( bool,			WriteQonversionDetails,		false			) \
    RTCONFIG_VARIABLE( bool,			OverwriteExistingFiles,		false			) \
	RTCONFIG_VARIABLE( string,			ProteinDatabase,			""				) \
	RTCONFIG_VARIABLE( string,			DecoyPrefix,				"rev_"			) \
	RTCONFIG_VARIABLE( double,			MaxFDR,						0.05			) \
	RTCONFIG_VARIABLE( double,			MaxImportFDR,				0.25			) \
	RTCONFIG_VARIABLE( int,				MaxResultRank,				3				) \
    RTCONFIG_VARIABLE( bool,            RerankMatches,              false           ) \
    RTCONFIG_VARIABLE( double,          TruePositiveThreshold,      0.01            ) \
    RTCONFIG_VARIABLE( int,             MaxTrainingRank,            1               ) \
    RTCONFIG_VARIABLE( bool,            PredictProbability,         true            ) \
    RTCONFIG_VARIABLE( double,          Gamma,                      5               ) \
    RTCONFIG_VARIABLE( double,          Nu,                         0.5             ) \
    RTCONFIG_VARIABLE( int,             PolynomialDegree,           3               ) \
    RTCONFIG_VARIABLE( string,			ScoreInfo,                  "1 off myrimatch:mvh; 1 off xcorr; 1 off sequest:xcorr; 1 off sequest:deltacn; 1 off mascot:score; -1 off x!tandem:expect; 1 off x!tandem:hyperscore; 1 off numBlindPTMs; 1 off numPTMs" ) \
    RTCONFIG_VARIABLE( Qonverter::QonverterMethod, QonverterMethod, "PartitionedSVM" ) \
    RTCONFIG_VARIABLE( Qonverter::SVMType, SVMType, "CSVC" ) \
    RTCONFIG_VARIABLE( Qonverter::Kernel, Kernel, "Linear" ) \
    RTCONFIG_VARIABLE( Qonverter::ChargeStateHandling, ChargeStateHandling, "Partition" ) \
    RTCONFIG_VARIABLE( Qonverter::TerminalSpecificityHandling, TerminalSpecificityHandling, "Partition" ) \
    RTCONFIG_VARIABLE( Qonverter::MissedCleavagesHandling, MissedCleavagesHandling, "Ignore" ) \
    RTCONFIG_VARIABLE( Qonverter::MassErrorHandling, MassErrorHandling, "Ignore" )

BEGIN_IDPICKER_NAMESPACE


struct PWIZ_API_DECL Version
{
    static int Major();
    static int Minor();
    static int Revision();
    static std::string str();
    static std::string LastModified();
};

enum QonvertErrorCode
{
	QONVERT_SUCCESS,
	QONVERT_ERROR_UNHANDLED_EXCEPTION,
	QONVERT_ERROR_FASTA_FILE_FAILURE,
	QONVERT_ERROR_RUNTIME_CONFIG_FILE_FAILURE,
	QONVERT_ERROR_RESIDUE_CONFIG_FILE_FAILURE,
	QONVERT_ERROR_NOT_ENOUGH_ARGUMENTS,
	QONVERT_ERROR_RUNTIME_CONFIG_OVERRIDE_FAILURE,
	QONVERT_ERROR_NO_INPUT_FILES_FOUND,
	QONVERT_ERROR_NO_TARGET_PROTEINS,
    QONVERT_ERROR_NO_DECOY_PROTEINS
};

static float EPSILON = 0.0001f;

struct RunTimeConfig : public freicore::BaseRunTimeConfig
{
public:
	RTCONFIG_DEFINE_MEMBERS( RunTimeConfig, IDPQONVERT_RUNTIME_CONFIG, "\r\n\t ", "idpQonvert.cfg", "\r\n#" )

    fileList_t inputFilepaths;
    map<string, Qonverter::Settings::ScoreInfo> scoreInfoByName;

    Qonverter::Settings getQonverterSettings()
    {
        Qonverter::Settings settings;
        settings.qonverterMethod = QonverterMethod;
        settings.svmType = SVMType;
        settings.kernel = Kernel;
        settings.chargeStateHandling = ChargeStateHandling;
        settings.terminalSpecificityHandling = TerminalSpecificityHandling;
        settings.missedCleavagesHandling = MissedCleavagesHandling;
        settings.massErrorHandling = MassErrorHandling;
        settings.decoyPrefix = DecoyPrefix;
        settings.scoreInfoByName = scoreInfoByName;
        settings.rerankMatches = RerankMatches;
        settings.truePositiveThreshold = TruePositiveThreshold;
        settings.maxTrainingRank = MaxTrainingRank;
        settings.predictProbability = PredictProbability;
        settings.gamma = Gamma;
        settings.nu = Nu;
        settings.degree = PolynomialDegree;
        return settings;
    }

private:
	void finalize()
	{
		vector<string> tokens;
		split(tokens, ScoreInfo, boost::is_any_of(";"));

        scoreInfoByName.clear();
		for (size_t i=0; i < tokens.size(); ++i)
		{
            to_lower(tokens[i]);
            trim(tokens[i]);
            vector<string> tokens2;
            split(tokens2, tokens[i], boost::is_space());

            if (tokens2.size() < 3)
                throw runtime_error("invalid score info (must be a space-separated triplet of <weight> <off|quantile|linear> <name>)");

            const string& weight = tokens2[0];
            const string& normalization = tokens2[1];
            string name = bal::join(boost::make_iterator_range(tokens2.begin()+2, tokens2.end()), " ");

            Qonverter::Settings::ScoreInfo& scoreInfo = scoreInfoByName[name];
            scoreInfo.weight = lexical_cast<double>(weight);
            scoreInfo.order = scoreInfo.weight >= 0 ? Qonverter::Settings::Order::Ascending
                                                    : Qonverter::Settings::Order::Descending;
            scoreInfo.weight = std::abs(scoreInfo.weight);

            scoreInfo.normalizationMethod = Qonverter::Settings::NormalizationMethod::Off;
            if (normalization == "quantile")
                scoreInfo.normalizationMethod = Qonverter::Settings::NormalizationMethod::Quantile;
            else if (normalization == "linear")
                scoreInfo.normalizationMethod = Qonverter::Settings::NormalizationMethod::Linear;
            else if (normalization != "off")
                throw runtime_error("invalid NormalizationMethod (must be 'off', 'quantile', or 'linear')");
		}
	}
};

extern RunTimeConfig* g_rtConfig;


END_IDPICKER_NAMESPACE

#endif
