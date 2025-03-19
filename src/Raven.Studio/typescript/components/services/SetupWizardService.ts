import getEulaCommand from "commands/licensing/getEulaCommand";
import continueUnsecureClusterConfigurationCommand from "commands/wizard/continueUnsecureClusterConfigurationCommand";
import extractNodesInfoFromPackageCommand from "commands/wizard/extractNodesInfoFromPackageCommand";
import finishSetupCommand from "commands/wizard/finishSetupCommand";
import listHostsForCertificateCommand from "commands/wizard/listHostsForCertificateCommand";
import registrationInfoCommand from "commands/wizard/registrationInfoCommand";

export default class SetupWizardService {
    async getEula() {
        return new getEulaCommand().execute();
    }

    async extractNodesInfoFromPackage(...args: ConstructorParameters<typeof extractNodesInfoFromPackageCommand>) {
        return new extractNodesInfoFromPackageCommand(...args).execute();
    }

    async registrationInfo(...args: ConstructorParameters<typeof registrationInfoCommand>) {
        return new registrationInfoCommand(...args).execute();
    }

    async listHostsForCertificate(...args: ConstructorParameters<typeof listHostsForCertificateCommand>) {
        return new listHostsForCertificateCommand(...args).execute();
    }

    async continueUnsecureClusterConfiguration(
        ...args: ConstructorParameters<typeof continueUnsecureClusterConfigurationCommand>
    ) {
        return new continueUnsecureClusterConfigurationCommand(...args).execute();
    }

    async finishSetup(...args: ConstructorParameters<typeof finishSetupCommand>) {
        return new finishSetupCommand(...args).execute();
    }
}
