import SetupWizardService from "components/services/SetupWizardService";
import { AutoMockService, MockedValue } from "./AutoMockService";
import { SetupWizardStubs } from "test/stubs/SetupWizardStubs";

export default class MockSetupWizardService extends AutoMockService<SetupWizardService> {
    constructor() {
        super(new SetupWizardService());
    }

    withEula(dto?: MockedValue<string>) {
        return this.mockResolvedValue(this.mocks.getEula, dto, SetupWizardStubs.eula());
    }

    withNodesInfoFromPackage(dto?: MockedValue<Raven.Server.Web.System.ConfigurationNodeInfo[]>) {
        return this.mockResolvedValue(
            this.mock.extractNodesInfoFromPackage,
            dto,
            SetupWizardStubs.nodesInfoFromPackage()
        );
    }

    withRegistrationInfo() {
        return this.mocks.registrationInfo.mockImplementation(async (license) => {
            // TODO all all types?

            if (license.Id === "Community") {
                return SetupWizardStubs.registrationInfoCommunity();
            }

            if (license.Id === "Essential") {
                return {
                    ...SetupWizardStubs.registrationInfoCommunity(),
                    LicenseType: "Essential",
                };
            }

            if (license.Id === "Enterprise") {
                return {
                    ...SetupWizardStubs.registrationInfoCommunity(),
                    LicenseType: "Enterprise",
                    MaxClusterSize: 2147483647,
                };
            }

            if (license.Id === "Developer") {
                return {
                    ...SetupWizardStubs.registrationInfoCommunity(),
                    LicenseType: "Developer",
                    MaxClusterSize: 3,
                };
            }

            if (license.Id === "Professional") {
                return {
                    ...SetupWizardStubs.registrationInfoCommunity(),
                    LicenseType: "Professional",
                    MaxClusterSize: 5,
                };
            }
        });
    }
}
