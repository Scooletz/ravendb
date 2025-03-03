import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import SetupWizardClickableCard from "../partials/SetupWizardClickableCard";

export default function SetupWizardSecurityStep() {
    const { control, setValue } = useFormContext<SetupWizardFormData>();

    const {
        securityStep: { securityOption },
    } = useWatch({ control });

    // TODO Add conditional recommended badge to Secure

    return (
        <div>
            <h2>Security</h2>
            <p>Select the security option that best addresses your needs</p>
            <div className="mt-4">
                <h5 className="mb-1">
                    <Icon icon="lock" color="success" />
                    Secure
                </h5>
                <SetupWizardClickableCard
                    icon="lets-encrypt"
                    title="Generate Let’s Encrypt certificate"
                    description="Secure and hassle-free communication with automatic certificate management"
                    isSelected={securityOption === "letsEncrypt"}
                    onClick={() => setValue("securityStep.securityOption", "letsEncrypt")}
                />
                <SetupWizardClickableCard
                    className="mt-2"
                    icon="certificate"
                    title="Provide your own certificate"
                    description="Ideal for secure corporate setups with manual certificate management"
                    isSelected={securityOption === "ownCertificate"}
                    onClick={() => setValue("securityStep.securityOption", "ownCertificate")}
                />
            </div>
            <div className="mt-4">
                <h5 className="mb-1">
                    <Icon icon="lock" color="warning" />
                    Unsecure
                </h5>
                <SetupWizardClickableCard
                    icon="empty-set"
                    title="Don’t use certificate"
                    description="Best for quick local development with no security requirements"
                    isSelected={securityOption === "none"}
                    onClick={() => setValue("securityStep.securityOption", "none")}
                />
            </div>
        </div>
    );
}
