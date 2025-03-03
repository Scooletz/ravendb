import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";

export default function SetupWizardSelfSignedCertificateStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { selfSignedCertificateStep } = useWatch({ control });

    return <h1>Self-signed certificate</h1>;
}
