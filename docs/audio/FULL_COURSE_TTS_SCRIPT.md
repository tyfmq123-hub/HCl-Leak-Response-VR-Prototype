# HCl VR 전체 교육 TTS 대본

## 적용 기준

- 음성: `ko-KR-SunHiNeural`
- 속도: `-8%`
- 음량: `-4%`
- 피치: `-1Hz`
- 형식: MP3, 2D 음성, Mono
- PPE, PreEntry, LeakResponse의 실제 진행 이벤트에서 재생한다.
- 씬 전환 시 이전 씬 음성을 정지하고, 같은 일회성 안내는 중복 재생하지 않는다.
- P-101~P-104 선택을 교육 내용으로 말하지 않고 `누출 의심 설비`로 안내한다.
- 긴 전체 자막은 만들지 않으며, 기존 단계 안내 UI만 음성과 같은 의미로 유지한다.

## PPE — `Assets/Resources/TrainingNarration/PPE`

| 파일 | 대본 |
|---|---|
| 01_EmergencyIntro | 염산 이송 설비에서 누출이 감지되었습니다. 현장에 바로 접근하지 말고, 보호구와 대응 장비를 준비한 뒤 진입 전 안전 상태를 확인하십시오. |
| 02_WearEssentialPPE | 앞쪽 화이트보드를 보십시오. 표시된 누출 설비의 위치와 연결된 배관을 잠시 살펴보십시오. 위치 확인을 마치면 보호구를 준비합니다. 아직 현장으로 이동하지 마십시오. |
| 03_WearInOrder | 착용 장비는 오른쪽 장비 보관대에 있습니다. 보관대를 보며 안내되는 순서대로 착용한 뒤, 대응가방과 수거용기를 준비하십시오. |
| 04_PointWithRay | 장비 가까이 손을 가져간 뒤 그립 버튼을 눌러 잡으십시오. |
| 05_OutlineShowsSelectable | 외곽선이 표시된 장비를 손으로 잡을 수 있습니다. |
| 06_GripSelectThenEquip | 그립 버튼을 누른 채 장비를 착용 위치로 옮긴 뒤, 그립 버튼에서 손을 떼어 착용하십시오. |
| 07_BButtonCancel | 잘못 놓친 장비는 원래 위치로 돌아갑니다. |
| 08_WearInnerGloves | 속장갑을 그립 버튼으로 잡으십시오. 반대쪽 손의 착용 위치로 옮긴 뒤 그립 버튼을 놓으십시오. 양손 모두 착용합니다. |
| 09_InnerGlovesEquipped | 속장갑 착용을 확인했습니다. |
| 10_WearChemicalSuit | 그립 버튼을 누른 채 화학보호복을 잡아 몸 앞의 착용 위치로 가져오십시오. 그립 버튼을 놓으면 착용됩니다. |
| 11_ChemicalSuitEquipped | 내산성 화학보호복 착용을 확인했습니다. |
| 12_WearChemicalBoots | 안전장화를 그립 버튼으로 잡아 표시된 발의 착용 위치로 옮긴 뒤, 그립 버튼을 놓으십시오. 양쪽 모두 착용합니다. |
| 13_ChemicalBootsEquipped | 내화학 안전장화 착용을 확인했습니다. |
| 14_WearRespirator | 호흡 보호구를 그립 버튼으로 잡아 얼굴의 착용 위치로 가져오십시오. 그립 버튼을 놓아 착용하십시오. |
| 15_RespiratorEquipped | 전면형 호흡 보호구 착용을 확인했습니다. |
| 16_WearFaceProtection | 안면보호구를 그립 버튼으로 잡아 얼굴의 착용 위치로 가져온 뒤, 그립 버튼을 놓으십시오. |
| 17_FaceProtectionEquipped | 안면보호구 착용을 확인했습니다. |
| 18_WearOuterGloves | 외부 보호장갑을 그립 버튼으로 잡으십시오. 반대쪽 손의 착용 위치로 옮긴 뒤 그립 버튼을 놓으십시오. 양손 모두 착용합니다. |
| 19_OuterGlovesEquipped | 외부 보호장갑 착용을 확인했습니다. |
| 20_EssentialPPEComplete | 필수 개인보호구 착용이 완료되었습니다. 이제 현장에서 사용할 대응 장비를 준비하십시오. |
| 21_PrepareResponseBag | 이제 대응가방에 장비를 넣습니다. 오른쪽 준비대의 측정기, 흡착패드, 흡착삭을 각각 그립 버튼으로 잡으십시오. 가방 입구로 가져간 뒤 그립 버튼을 놓아 수납하십시오. |
| 22_ResponseBagReady | 대응가방 준비를 확인했습니다. |
| 23_PrepareWasteContainer | 오른쪽의 노란색 폐기물 수거용기를 바라보십시오. 선택 표시가 나타나면 그립 버튼을 눌러 준비하십시오. |
| 24_WasteContainerReady | 오염 폐기물 수거용기 준비를 확인했습니다. |
| 25_CheckingPrepStatus | 현장 진입 전 준비 상태를 확인합니다. |
| 26_AllFieldPrepComplete | 개인보호구와 대응 장비 준비가 모두 완료되었습니다. |
| 27_GoToFieldEntrance | 오른손 에이 버튼을 눌러 현장 진입 전 안전 확인 단계로 이동하십시오. |
| 28_MovingToFieldEntrance | 현장 입구로 이동합니다. |
| 29_WrongPPEOrder | 착용 순서가 올바르지 않습니다. 현재 안내 중인 보호구를 먼저 착용하십시오. |
| 30_WearGlovesFirst | 먼저 속장갑을 착용하십시오. |
| 31_OuterGlovesAfterRespirator | 외부 보호장갑은 호흡 보호구와 안면보호구를 착용한 뒤 착용하십시오. |
| 32_AlreadyEquipped | 이미 착용하거나 준비한 장비입니다. |
| 33_CompleteEssentialPPEFirst | 대응 장비를 준비하기 전에 필수 개인보호구 착용을 완료하십시오. |
| 34_MissingPPE | 필수 개인보호구가 누락되었습니다. 화이트보드의 준비 순서를 다시 확인하십시오. |
| 35_PrepareResponseBagFirst | 대응가방 안에 필요한 장비를 모두 수납한 뒤 가방을 챙기십시오. |
| 36_CheckCurrentRequiredPPE | 현재 안내 중인 개인보호구를 확인하십시오. |
| 37_GripPackedResponseBag | 세 가지 장비를 모두 수납했습니다. 그립 버튼을 한 번 눌러 대응가방을 챙기십시오. |

## PreEntry — `Assets/Resources/TrainingNarration/PreEntry`

| 파일 | 대본 |
|---|---|
| 01_SceneIntro | 현장에 접근하기 전에 염화수소 농도와 풍향을 확인하고, 누출 의심 설비의 운전 상태와 잔류 압력을 점검해야 합니다. |
| 02_EquipDetector | 왼손을 가방 위로 가져가 그립 버튼을 한 번 눌렀다 놓으십시오. 측정기가 올라오면 고스트 손 위치에서 왼손 그립 버튼을 다시 눌러 잡으십시오. |
| 03_MeasureInitialHcl | 측정기 화면을 보며 왼쪽 트리거를 삼 초간 유지하여 현재 염화수소 농도를 측정하십시오. |
| 04_ConfirmInitialHcl | 염화수소 농도가 높게 측정되었습니다. 지금은 현장에 접근하지 마십시오. |
| 05_CheckInitialWind | 표시된 풍향계를 바라보십시오. 시선을 삼 초간 유지하여 유해 증기가 이동하는 방향을 확인합니다. 버튼은 누르지 않아도 됩니다. |
| 06_WaitWindChange | 현재 풍향에서는 유해 증기가 접근 위치로 유입될 수 있습니다. 안전한 풍향으로 바뀔 때까지 대기하십시오. |
| 07_CheckChangedWind | 풍향이 바뀌었습니다. 풍향계를 다시 바라보고 시선을 삼 초간 유지하십시오. |
| 08_MeasureReducedHcl | 풍향이 바뀌었더라도 진입 전 농도를 다시 확인해야 합니다. 왼쪽 트리거를 삼 초간 유지하여 염화수소 농도를 재측정하십시오. |
| 09_ConfirmReducedHcl | 염화수소 농도가 낮아진 것을 확인했습니다. |
| 10_StoreDetector | 왼손 그립을 눌러 측정기를 가방에 수납하십시오. |
| 11_MoveToPanel | 측정기를 수납했습니다. 이제 오른손 에이 버튼을 눌러 설비 상태 확인 패널 앞으로 이동하십시오. |
| 12_CheckPressure | 누출 의심 설비의 압력과 온도, 운전 상태를 순서대로 확인합니다. 먼저 압력 표시를 이 초간 확인하십시오. |
| 13_CheckTemperature | 온도 표시를 이 초간 확인하십시오. |
| 14_CheckRunState | 설비가 현재 운전 중인지 확인합니다. 런 표시를 이 초간 확인하십시오. |
| 15_StopPump | 설비가 운전 중입니다. 해당 열의 스톱 버튼에 오른손을 넣고 오른쪽 트리거를 눌러 펌프를 정지하십시오. |
| 16_VerifyStopIndicator | 펌프 정지 상태가 정상적으로 표시되는지 스톱 표시를 이 초간 확인하십시오. |
| 17_WaitPressureDecrease | 펌프를 정지한 뒤에도 배관 압력은 즉시 사라지지 않습니다. 압력이 감소할 때까지 기다리십시오. |
| 18_VerifyPressureDecrease | 배관 압력이 감소했습니다. 감소한 압력 표시를 이 초간 확인하십시오. |
| 19_SafetyCheckComplete | 염화수소 농도와 풍향, 설비 정지와 압력 감소까지 확인했습니다. 현장 진입 전 안전 확인이 완료되었습니다. |
| 20_MoveToLeakResponse | 방호벽은 유지됩니다. 지정된 이동 지점에서 오른손 에이 버튼을 눌러 누출 대응 구역으로 이동하십시오. |

## LeakResponse — `Assets/Resources/TrainingNarration/LeakResponse`

| 파일 | 대본 |
|---|---|
| 01_LeakSceneArrival | 누출 대응 구역에 진입했습니다. 미스트와 액체가 발생하는 플랜지에 직접 접근하지 말고, 먼저 설비 양쪽의 흡입측과 토출측 밸브를 차단하십시오. |
| 02_SuctionValveActivated | 양손 그립으로 흡입측 밸브를 잡아 시계 방향으로 돌리고, 게이지를 끝까지 채우십시오. |
| 03_SuctionValveClosed | 흡입측 밸브가 닫혔습니다. |
| 04_DischargeValveActivated | 양손 그립으로 토출측 밸브를 잡아 시계 방향으로 돌리고, 게이지를 끝까지 채우십시오. |
| 05_BothValvesClosed | 흡입측과 토출측 밸브가 모두 닫혔습니다. |
| 06_LeakReductionStarted | 누출량이 감소하고 있습니다. 표시된 플랜지를 바라보고 시선을 삼 초간 유지하십시오. 버튼은 누르지 않아도 됩니다. |
| 07_LeakReductionCompleted | 누출 차단 상태를 확인했습니다. 이제 바닥으로 확산되는 액체를 흡착재로 차단합니다. |
| 08_AbsorbentSockPlacementActivated | 가방 위에서 그립 버튼을 한 번 눌렀다 놓아 흡착삭을 꺼내십시오. 다시 그립 버튼으로 잡아 누출 주변의 표시 위치로 옮긴 뒤, 그립 버튼을 놓아 설치하십시오. |
| 09_AllAbsorbentSocksPlaced | 흡착삭 설치가 완료되었습니다. |
| 10_AbsorbentPadPlacementActivated | 가방 위에서 그립 버튼을 한 번 눌렀다 놓아 흡착패드를 꺼내십시오. 다시 그립 버튼으로 잡아 흡착삭 안쪽의 표시 위치로 옮긴 뒤, 그립 버튼을 놓아 설치하십시오. |
| 11_AllAbsorbentPadsPlaced | 흡착패드 설치가 완료되었습니다. |
| 12_AbsorptionCompleted | 흡착 대응이 완료되었습니다. 오염된 흡착재를 직접 손으로 만지지 마십시오. 이제 수거 절차를 진행합니다. |
| 13_ContainerAndLidGhostActivated | 수거용기 뚜껑을 그립 버튼으로 잡으십시오. 옆의 고스트 표시 위치로 옮긴 뒤 그립 버튼을 놓아 보관하십시오. |
| 14_ContainerLidStowed | 뚜껑 보관이 완료되었습니다. 이제 옆에 있는 집게를 그립으로 잡으십시오. |
| 15_RecoveryTongsPickedUp | 집게를 잡았습니다. 이제 그립 버튼을 놓아도 집게는 손에 유지됩니다. 흡착패드에 집게 끝을 대고 트리거를 누른 채 집으십시오. 수거용기 안에서 트리거를 놓아 넣으십시오. |
| 16_PadRecoveryIdle | 집게 끝을 흡착패드에 대고 트리거를 누른 채 집으십시오. 수거용기 안에서 트리거를 놓으십시오. |
| 17_AllPadsRecovered | 흡착패드를 모두 수거했습니다. |
| 18_SockRecoveryStarted | 이제 흡착삭을 수거합니다. 집게 끝을 대고 트리거를 누른 채 집으십시오. 수거용기 안에서 트리거를 놓으십시오. |
| 19_SockRecoveryIdle | 트리거를 눌러 남은 흡착삭을 집게로 수거하십시오. |
| 20_AllSocksRecovered | 오염 흡착재를 모두 수거했습니다. 옆에 집게 반납 위치가 표시됩니다. 집게를 그 위치에 맞춘 뒤, 집게를 든 손의 그립 버튼을 한 번 눌러 반납하십시오. |
| 21_RecoveryTongsReturned | 집게를 반납했습니다. |
| 22_ContainerClosureGhostActivated | 거치된 뚜껑을 그립 버튼으로 잡으십시오. 수거용기 위의 닫힘 표시 위치로 옮긴 뒤 그립 버튼을 놓아 밀폐하십시오. |
| 23_ContainerClosedTaskCompleted | 오염 폐기물 용기가 밀폐되었습니다. 염산 누출 대응 절차가 완료되었습니다. |
| 24_DirectContactAttempted | 오염된 흡착재를 손으로 직접 만지지 마십시오. 반드시 집게를 사용하십시오. |
| 25_SockRecoveryBeforePadsAttempted | 흡착패드를 먼저 수거한 뒤 흡착삭을 수거하십시오. |
| 26_ContaminatedAbsorbentDroppedOutside | 오염된 흡착재가 수거용기 밖에 놓였습니다. 집게로 다시 집어 용기 안에 넣으십시오. |

## Complete — `Assets/Resources/App/TTS`

| 파일 | 대본 |
|---|---|
| CompletionTrainingComplete | 누출 대응 교육이 완료되었습니다. 교육 결과를 확인해 주세요. |

## 향후 음성 교체 방법

같은 폴더와 같은 파일명을 유지한 채 MP3만 교체하면 코드나 씬 참조를 다시 연결할 필요가 없다. 새 단계를 추가할 때는 기존 번호를 바꾸지 말고 다음 번호로 추가하고, 해당 Stage의 cue enum과 진행 이벤트를 함께 연결한다.
