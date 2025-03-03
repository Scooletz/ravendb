import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { FormSelect } from "components/common/Form";
import { SelectOption } from "components/common/select/Select";
export default function SetupWizardSecurityStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { securityStep } = useWatch({ control });

    return (
        <div>
            <FormSelect control={control} name="securityStep.securityOption" options={securityOptions} />
        </div>
    );
}

const securityOptions: SelectOption<SetupWizardFormData["securityStep"]["securityOption"]>[] = [
    { value: "letsEncrypt", label: "Let's Encrypt" },
    { value: "ownCertificate", label: "Own certificate" },
    { value: "none", label: "None" },
];
