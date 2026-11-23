# Judge check (spec 0004, P6)

Run 2026-11-23 22:19 UTC, judge `claude-haiku-4-5`, answers `claude-sonnet-5-5` (effort Medium), commit `e08fff8-dirty`.
Score each answer 1–5 yourself in the **Owner** column, without looking at the judge's score first if you can.
Agreement = your score within 1 point of the judge's. Leave a note where you disagree.

| # | Question | Metric | Judge | Owner | Note |
|---|----------|--------|-------|-------|------|
| 1 | cf-05 | Relevance | 3 | | |
| 2 | kyc-01 | Relevance | 4 | | |
| 3 | cf-01 | Groundedness | 5 | | |
| 4 | dpdp-01 | Relevance | 4 | | |
| 5 | rbc-01 | Groundedness | 5 | | |
| 6 | kyc-02 | Relevance | 4 | | |
| 7 | cf-02 | Groundedness | 5 | | |
| 8 | dpdp-02 | Relevance | 5 | | |
| 9 | rbc-02 | Groundedness | 5 | | |
| 10 | kyc-03 | Relevance | 4 | | |

## Answers and the judge's reasons

### cf-05: What checks must an NBFC do before it partners with a lending service provider?

The passages do not set out pre-engagement due diligence checks for choosing an LSP. They only say an NBFC must ensure that it and the LSPs it engages comply with cybersecurity technology standards, and, where an LSP has several lenders, that the LSP follows the stated requirements. These are ongoing compliance obligations, not a checklist of checks before partnering.

Cited: rbi-nbfc-credit-facilities-2025 Para 16; rbi-nbfc-credit-facilities-2025 Para 7

- **Groundedness 5**: The RESPONSE is fully grounded in the provided CONTEXT. It accurately identifies that while the passages contain requirements for NBFCs and LSPs to comply with cybersecurity standards (Para 16) and requirements for how LSPs must operate when they have multiple lender arrangements (Para 7), the CONTEXT does not explicitly provide a checklist of pre-engagement due diligence checks that NBFCs must perform before selecting or partnering with an LSP. The response correctly distinguishes between pre-engagement checks and ongoing compliance obligations, and accurately reflects the content of the passages provided.
- **Relevance 3**: The response directly addresses the query by clarifying that the available passages do not specify pre-engagement due diligence checks for choosing an LSP. It accurately identifies what information IS available (ongoing cybersecurity compliance obligations) and distinguishes these from pre-partnership checks. However, while the response provides helpful insight into the nature of the available information, it ultimately does not provide the specific checks an NBFC must perform, which is what the user asked for. The response is accurate and somewhat insightful, but incomplete in addressing the core question.

### kyc-01: What extra steps must an NBFC take before opening an account for a politically exposed person?

Besides normal customer due diligence, an NBFC that chooses to establish a relationship with a PEP must have risk management systems to determine whether the customer or beneficial owner is a PEP, take reasonable measures to establish the source of funds or wealth, and get senior management approval before opening the account. It must also subject the account to enhanced ongoing monitoring. If an existing customer later becomes a PEP, senior management approval is needed to continue the relationship. These requirements also apply to family members and close associates of PEPs.

Cited: rbi-nbfc-kyc-2025 Para 45; rbi-nbfc-kyc-2025 Para 45; rbi-nbfc-kyc-2025 Para 45; rbi-nbfc-kyc-2025 Para 45; rbi-nbfc-kyc-2025 Para 45; rbi-nbfc-kyc-2025 Para 45

- **Groundedness 5**: The response is fully grounded in the provided context. It accurately identifies and comprehensively addresses all the extra steps that an NBFC must take when opening an account for a PEP according to Para 45 of the RBI Directions. The response includes all five specific requirements (risk management systems, source of funds verification, senior management approval, enhanced monitoring, and provisions for existing customers becoming PEPs), correctly references the extension to family members and close associates, and provides this information with precision and completeness without introducing any unsupported or incorrect details.
- **Relevance 4**: The response comprehensively addresses the query by detailing all major extra steps required for NBFCs opening accounts for PEPs: risk management systems for identification, source of funds verification, senior management approval, enhanced ongoing monitoring, and special handling for PEP family members and associates. It is accurate, well-structured, and includes relevant context about existing customers becoming PEPs. The information is complete without unnecessary elaboration, making it both thorough and focused on the question asked.

### cf-01: Can a loan app go through the people saved in my phone?

No. An NBFC must ensure that its loan app, and the apps of its lending service providers, do not access the contact list on your phone. Data collection must be need-based and made with your prior, explicit consent. The only exception is one-time access to the camera, microphone, location or similar facility, and only for onboarding or KYC, with your explicit consent.

Cited: rbi-nbfc-credit-facilities-2025 Para 13; rbi-nbfc-credit-facilities-2025 Para 13; rbi-nbfc-credit-facilities-2025 Para 13

- **Groundedness 5**: The response is fully grounded in the provided context. It accurately answers the query by directly referencing Para 13(1) of the RBI Directions, correctly stating that loan apps cannot access contact lists, explaining the requirement for need-based data collection with explicit consent, and accurately identifying the limited exceptions (camera, microphone, location) that apply only for onboarding/KYC purposes. All information is precisely sourced from the context with no incorrect or extraneous details.
- **Relevance 5**: The response earns a high score because it directly and completely answers the query ("No") while providing essential regulatory context (NBFC requirements), explaining the underlying principle (need-based, consent-required data collection), and clarifying the limited exceptions. The answer is accurate, comprehensive, and includes relevant elaboration that enhances understanding without being extraneous.

### dpdp-01: If a company's customer database is hacked, who must it tell and how quickly?

Under the Digital Personal Data Protection Rules, a Data Fiduciary that becomes aware of a personal data breach must tell each affected Data Principal without delay, by her user account or a communication mode she registered. It must also tell the Data Protection Board without delay, giving a description of the breach. Within 72 hours of becoming aware (or a longer period the Board allows on a written request), it must give the Board updated and detailed information, including the facts, mitigation measures, findings on who caused it, remedial measures and a report on the intimations given to affected individuals. The passages do not say whether any NBFC-specific RBI reporting (such as to the RBI or CERT-In) applies to a customer database hack.

Cited: dpdp-rules-2025 Rule 7; dpdp-rules-2025 Rule 7; dpdp-rules-2025 Rule 7

- **Groundedness 5**: The response is fully correct and complete. It accurately cites the Digital Personal Data Protection Rules, 2025, Rule 7, and comprehensively answers both parts of the query: (1) who must be told (affected Data Principals and the Data Protection Board), and (2) how quickly (without delay, with 72-hour requirement for detailed information to the Board). The response includes all relevant details from the context without adding unrelated information, and appropriately acknowledges the scope limitations regarding NBFC-specific requirements.
- **Relevance 4**: The response directly and accurately addresses both parts of the query (who must be notified and how quickly). It provides specific regulatory framework details with concrete timelines (immediate "without delay" notification plus 72-hour detailed reporting requirement to the Board), mentions multiple notification recipients, and specifies communication methods. The response fully addresses the question's essential requirements while appropriately acknowledging uncertainties about additional regulatory requirements. This constitutes a complete, well-structured answer with minor elaboration, fitting Level 4-5 criteria.

### rbc-01: What must a Key Fact Statement contain?

The KFS must follow the standardised format in Annex I and be written in a language the borrower understands. It must carry a unique proposal number and a minimum validity period, and include an APR computation sheet and the loan's amortisation schedule. Annex I Part 1 covers interest rate and fees/charges: sanctioned amount, disbursal schedule, loan term, instalment details, interest rate and type, floating-rate details, fees and charges, APR, and contingent charges such as penal and foreclosure charges. Part 2 covers qualitative information: clauses on recovery agents and grievance redressal, nodal officer contact details, whether the loan may be transferred or securitised, collaborative lending details, and digital loan disclosures.

Cited: rbi-nbfc-responsible-business-conduct-2025 Para 29, part 1 of 3; rbi-nbfc-responsible-business-conduct-2025 Para 29, part 1 of 3; rbi-nbfc-responsible-business-conduct-2025 Para 29, part 1 of 3; rbi-nbfc-responsible-business-conduct-2025 Para 29, part 1 of 3; rbi-nbfc-responsible-business-conduct-2025 Annex I, part 1 of 2; rbi-nbfc-responsible-business-conduct-2025 Annex I, part 1 of 2; rbi-nbfc-responsible-business-conduct-2025 Annex I, part 2 of 2; rbi-nbfc-responsible-business-conduct-2025 Annex I, part 2 of 2

- **Groundedness 5**: The response is fully grounded in the provided context. It accurately and comprehensively answers the query about KFS contents by directly drawing from Para 29 and Annex I of the RBI Directions. The response includes all key requirements: the standardised format, language requirement, unique proposal number, validity period, APR computation, amortisation schedule, and both Part 1 (interest/fees/charges details) and Part 2 (qualitative information) contents. The information is presented accurately without errors or unsupported additions, and covers the complete scope of what must be contained in a KFS according to the regulatory framework provided.
- **Relevance 5**: The response comprehensively addresses the query by providing complete information about what a KFS must contain. It includes the standardized format requirement, specific document components (APR sheet, amortisation schedule), unique identifiers (proposal number), and systematically breaks down both parts of the required Annex I with detailed sub-components. The information is well-organized and provides sufficient elaboration to fully understand KFS requirements without extraneous information.

### kyc-02: How often must an NBFC review the risk categorisation of its customers' accounts?

An NBFC must have a system of periodic review of the risk categorisation of customer accounts, with the review done at least once in every six months.

Cited: rbi-nbfc-kyc-2025 Para 41

- **Groundedness 5**: The response directly and accurately answers the query by citing the exact requirement from Para 41(1) of the Reserve Bank of India Directions. It correctly states that an NBFC must review risk categorisation "at least once in every six months," which matches the context precisely. The response includes all relevant details needed to fully answer the question and contains no incorrect or extraneous information.
- **Relevance 4**: The response directly and accurately answers the specific question about NBFC review frequency with the key information: reviews must occur at least once every six months. It includes all essential details needed to fully understand the requirement without adding unnecessary elaboration or insights. This represents a complete, accurate response that fully addresses the query.

### cf-02: What is the cooling-off period for digital loans?

For digital loans, the borrower must get an explicit option to exit during an initial cooling-off period by paying the principal and the proportionate APR, without penalty. The NBFC decides the length in its credit policy, but it cannot be less than one day. The NBFC may keep a reasonable one-time processing fee if the customer exits in this period, and it must disclose this upfront in the KFS.

Cited: rbi-nbfc-credit-facilities-2025 Para 11; rbi-nbfc-credit-facilities-2025 Para 11; rbi-nbfc-credit-facilities-2025 Para 11

- **Groundedness 5**: The response thoroughly and accurately addresses the query by providing all relevant details from Para 11 of the context. It correctly explains that the cooling-off period is determined by the NBFC with a minimum of one day, includes the exit mechanism (paying principal and proportionate APR without penalty), and mentions the optional processing fee with disclosure requirement. All information is directly supported by the context, with no inaccuracies or omissions of key details needed to fully answer the question about cooling-off periods for digital loans.
- **Relevance 5**: The response comprehensively addresses the query by providing the core answer (cooling-off period minimum of one day, determined by NBFC policy), while also including complete details about the conditions, fees, and regulatory disclosure requirements. The information is accurate and directly relevant, and it goes beyond the minimum by explaining the broader context and implications of the cooling-off period, making it more informative than just stating a time period.

### dpdp-02: What must a notice from a Data Fiduciary to a Data Principal contain?

Under Rule 3, the notice must be presented and be understandable on its own, independent of other information from the Data Fiduciary. It must give, in clear and plain language, a fair account of the details needed for specific and informed consent, including at minimum an itemised description of the personal data and the specified purpose(s), with a specific description of the goods or services or uses enabled by the processing. It must also give the communication link to the Data Fiduciary's website or app, and describe any other means by which the Data Principal may withdraw consent (as easily as it was given), exercise her rights under the Act, and complain to the Board.

Cited: dpdp-rules-2025 Rule 3; dpdp-rules-2025 Rule 3; dpdp-rules-2025 Rule 3; dpdp-rules-2025 Rule 3; dpdp-rules-2025 Rule 3; dpdp-rules-2025 Rule 3; dpdp-rules-2025 Rule 3

- **Groundedness 5**: The response is fully grounded in the provided context. It comprehensively addresses the query by directly referencing Rule 3 and accurately capturing all three main requirements for notices from Data Fiduciaries to Data Principals: (1) independence and understandability, (2) clear language with itemized descriptions and specified purposes, and (3) communication links and means for consent withdrawal and rights exercise. The response is accurate, complete, and contains no extraneous or contradictory information.
- **Relevance 5**: The response thoroughly and accurately addresses the query by detailing all mandatory components of a notice from a Data Fiduciary to a Data Principal. It includes comprehensive information about presentation requirements, language clarity, content specifics (personal data itemization, purposes, goods/services), and procedural elements (withdrawal, rights exercise, complaint mechanisms). The response demonstrates understanding not just of what must be included, but also contextual elements like "specific and informed consent," which adds insight into the regulatory intent. No significant omissions or inaccuracies are evident.

### rbc-02: Can an NBFC add penal interest to my loan's interest rate when I miss a payment?

No. If an NBFC charges a penalty for a missed payment, it must be levied as 'penal charges', not as 'penal interest' added to the loan's interest rate. Penal charges should be reasonable and applied only on the amount in default. They cannot be capitalised, so no further interest is charged on them. For unpaid interest or EMI, the NBFC may still charge interest at the contracted rate until the default is remedied, but not at a penal rate. The rules apply to fresh loans from April 1, 2024 and to existing loans from June 30, 2024, and they exclude credit cards and certain other products.

Cited: rbi-nbfc-responsible-business-conduct-2025 Para 30, part 1 of 3; rbi-nbfc-responsible-business-conduct-2025 Para 30, part 1 of 3; rbi-nbfc-responsible-business-conduct-2025 Para 30, part 2 of 3; rbi-nbfc-responsible-business-conduct-2025 Para 30, part 1 of 3

- **Groundedness 5**: The response is fully correct and comprehensive. It directly answers the query with a clear "No" and then provides accurate, well-supported details about how penal charges must be treated under RBI guidelines. All key points (prohibition on penal interest, requirement for reasonable penal charges, non-capitalisation, treatment of unpaid interest, applicability dates, and exclusions) are explicitly mentioned in the provided context. The response demonstrates complete understanding of the material and addresses all relevant aspects of the question without introducing any extraneous or incorrect information.
- **Relevance 5**: The response deserves a high score because it not only directly and accurately answers the core question ("No, an NBFC cannot add penal interest to the loan's interest rate") but also provides comprehensive supporting details. It clarifies the distinction between prohibited "penal interest" and allowed "penal charges," explains how charges should be applied, mentions regulatory timelines, and identifies exclusions. This goes beyond a complete answer by including relevant context and regulatory details that enhance understanding of the user's rights and obligations.

### kyc-03: What is the Principal Officer responsible for under the KYC Directions?

The Principal Officer is responsible for ensuring compliance, monitoring transactions, and sharing and reporting information as required under the law and regulations. The NBFC must also tell FIU-IND and RBI the Principal Officer's name, designation, address and contact details.

Cited: rbi-nbfc-kyc-2025 Para 15; rbi-nbfc-kyc-2025 Para 15

- **Groundedness 5**: The response directly and accurately answers the query by providing the exact responsibilities of the Principal Officer as stated in Para 15(1) of the context: ensuring compliance, monitoring transactions, and sharing/reporting information as required under law and regulations. The response is fully grounded in the provided context, accurate in all details, and comprehensively addresses the question. The additional mention of communicating the Principal Officer's details to authorities is properly supported by Para 15(2), though it represents an NBFC obligation rather than a Principal Officer responsibility per se.
- **Relevance 4**: The response addresses the query by identifying the Principal Officer's main responsibilities under KYC Directions: ensuring compliance, monitoring transactions, and reporting information. However, the response conflates two separate matters—the officer's responsibilities and the requirement to disclose the officer's details to regulators. While the core responsibilities are covered accurately, without access to the full KYC Directions, it's difficult to assess if all significant responsibilities are included. The response provides essential information but lacks elaboration or additional insights that would strengthen understanding.

