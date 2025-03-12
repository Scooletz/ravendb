import getEulaCommand from "commands/licensing/getEulaCommand";
import extractNodesInfoFromPackageCommand from "commands/wizard/extractNodesInfoFromPackageCommand";

export default class SetupWizardService {
    async getEula() {
        return new getEulaCommand().execute();
    }

    async extractNodesInfoFromPackage(...args: ConstructorParameters<typeof extractNodesInfoFromPackageCommand>) {
        return new extractNodesInfoFromPackageCommand(...args).execute();
    }
}
