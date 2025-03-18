import getEulaCommand from "commands/licensing/getEulaCommand";
import extractNodesInfoFromPackageCommand from "commands/wizard/extractNodesInfoFromPackageCommand";
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
}
