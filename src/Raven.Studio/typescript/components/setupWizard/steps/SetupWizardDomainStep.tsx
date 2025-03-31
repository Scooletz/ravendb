import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import { FormGroup, FormLabel, FormSelect, FormSelectCreatable } from "components/common/Form";
import { SelectOption } from "components/common/select/Select";
import React, { useEffect, useState } from "react";
import { useAsyncCallback } from "react-async-hook";
import { useServices } from "hooks/useServices";
import RichAlert from "components/common/RichAlert";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import InputGroupText from "react-bootstrap/InputGroupText";
import ButtonWithSpinner from "components/common/ButtonWithSpinner";

export function SetupWizardDomainStep() {
    const { control, setValue, setError, clearErrors } = useFormContext<SetupWizardFormData>();

    const { domainStep, licenseKeyStep } = useWatch({ control });
    const { setupWizardService } = useServices();
    const { licenseInfo } = licenseKeyStep;
    const [domainsOptions, setDomainsOptions] = useState<SelectOption[]>(
        Object.keys(licenseInfo?.userDomainsWithIps?.domains ?? []).map((domain) => ({
            value: domain,
            label: domain,
        }))
    );

    useDomainFormSideEffects();

    const hasDnsRecords =
        domainStep.domain && Object.keys(licenseInfo?.userDomainsWithIps?.domains ?? []).includes(domainStep.domain);

    const asyncCheckDomainAvailability = useAsyncCallback(async (domain: string) => {
        const key = JSON.parse(licenseKeyStep.key) ?? "";

        return setupWizardService.checkDomainAvailability(domain, key);
    });

    const handleDomainAvailability = async (domain: string) => {
        const newOption = createNewOption(domain);
        clearErrors("domainStep.domain");
        try {
            const domainAvailability = await asyncCheckDomainAvailability.execute(domain);

            if (domainAvailability.Available) {
                clearErrors("domainStep.domain");
                setDomainsOptions((prev) => [...prev, newOption]);
                setValue("domainStep.domain", domain);
                return domain;
            } else {
                setError("domainStep.domain", {
                    type: "value",
                    message: "Domain is already in use",
                });
                return domain;
            }
        } catch (e) {
            // TODO handle error when api is not available
            console.error("Error", e);
        }
    };

    const rootDomainOptions = licenseInfo.userDomainsWithIps.rootDomains.map((domain) => ({
        value: domain,
        label: domain,
    }));

    return (
        <div>
            <h2>Domain</h2>
            <p>
                Enter your own domain to be linked with our default hosting zone, where your database will be hosted and
                protected with a security certificate.
            </p>
            <FormGroup>
                <FormLabel className="hstack justify-content-between">
                    <div>Domain</div>
                    <PopoverWithHoverWrapper
                        message={
                            <>
                                <p>
                                    The inserted domain is the domain name of your RavenDB instance. Let’s Encrypt will
                                    use this domain to create an SSL certificate for secure connections. Make sure the
                                    domain is accessible for certificate generation.
                                </p>
                                <RichAlert icon="info" variant="info">
                                    Domain name can only contain A-Z, a-z, 0-9, ‘-’ characters.
                                </RichAlert>
                            </>
                        }
                    >
                        <div className="text-info">
                            <Icon icon="info" /> What is this?
                        </div>
                    </PopoverWithHoverWrapper>
                </FormLabel>
                <FormSelectCreatable
                    isLoading={asyncCheckDomainAvailability.loading}
                    onCreateOption={handleDomainAvailability}
                    control={control}
                    name="domainStep.domain"
                    options={domainsOptions}
                    addon={
                        <FormSelect
                            control={control}
                            name="domainStep.rootDomain"
                            options={rootDomainOptions}
                            isSearchable={false}
                        />
                    }
                />
            </FormGroup>
            {hasDnsRecords && (
                <RichAlert icon="info" variant="info">
                    There are some DNS records already set for the selected domain. You will be able to overwrite these
                    settings in the next step.
                </RichAlert>
            )}
            <FormGroup className="mt-3">
                <FormLabel className="hstack justify-content-between">
                    <div>Email</div>
                    <PopoverWithHoverWrapper message="This email address is assigned to the license you’re currently using.">
                        <div className="text-info">
                            <Icon icon="info" /> What is this?
                        </div>
                    </PopoverWithHoverWrapper>
                </FormLabel>
                {/* height is set to 38px to match the height of the input field*/}
                {/*
                    How to present all email addresses associated with the license?
                    https://www.figma.com/design/3XmG4txhRal5GD09d43gSP?node-id=15-12475#1192386735
                 */}
                <InputGroupText style={{ height: "38px" }}>{domainStep.email}</InputGroupText>
            </FormGroup>
        </div>
    );
}

const useDomainFormSideEffects = () => {
    const { setValue, control } = useFormContext<SetupWizardFormData>();
    const {
        licenseKeyStep: { licenseInfo },
    } = useWatch({ control });

    useEffect(() => {
        if (licenseInfo?.userDomainsWithIps?.email.length === 1) {
            setValue("domainStep.email", licenseInfo.userDomainsWithIps.email[0]);
        }

        if (licenseInfo?.userDomainsWithIps?.rootDomains.length > 1) {
            setValue("domainStep.rootDomain", licenseInfo.userDomainsWithIps.rootDomains[0]); // select first root domain as default
        }

        if (Object.keys(licenseInfo?.userDomainsWithIps?.domains ?? []).length === 1) {
            setValue("domainStep.domain", Object.keys(licenseInfo.userDomainsWithIps.domains)[0]);
        }
    }, []); // eslint-disable-line react-hooks/exhaustive-deps
};

const createNewOption = (label: string) => ({
    label,
    value: label,
});

export function SetupWizardDomainStepFooter() {
    const { setValue, control } = useFormContext<SetupWizardFormData>();
    const { setupWizardService } = useServices();
    const { domainStep, licenseKeyStep } = useWatch({ control });

    const asyncClaimDomain = useAsyncCallback(async () => {
        const domain = domainStep.domain;
        const key = JSON.parse(licenseKeyStep.key);
        
        // @ts-expect-error when validation will be fixed, ts error will disappear TODO: remove.
        if (!licenseKeyStep.licenseInfo.userDomainsWithIps.domains[domain]) {
            await setupWizardService.claimDomain(domain, key);
        }
        
        setValue("currentStep", "Node address");
    });
    
    
    const handleContinue = async () => {
        await asyncClaimDomain.execute()
        
        setValue("currentStep", "Node address");
        
    };

    return (
        <div className="hstack justify-content-end">
            <ButtonWithSpinner isSpinning={asyncClaimDomain.loading} variant="primary" className="rounded-pill" onClick={handleContinue}>
                Continue <Icon icon="arrow-right" margin="m-0" />
            </ButtonWithSpinner>
        </div>
    );
}
