﻿using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using SAI.SAI.App.Models.Events;
using SAI.SAI.App.Presenters;
using SAI.SAI.App.Views.Interfaces;
using SAI.SAI.Application.Interop;
using System.Text.Json;
using SAI.SAI.App.Views.Common;
using SAI.SAI.App.Forms.Dialogs;
using SAI.SAI.App.Models;
using static SAI.SAI.App.Models.BlocklyModel;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Timer = System.Windows.Forms.Timer;
using SAI.SAI.Application.Service;
using System.Diagnostics;

namespace SAI.SAI.App.Views.Pages
{
    public partial class UcPracticeBlockCode : UserControl, IUcShowDialogView, IBlocklyView, IYoloTutorialView, IYoloPracticeView, IPracticeInferenceView
    {
        private BlocklyPresenter blocklyPresenter;
        private UcShowDialogPresenter ucShowDialogPresenter;
        private DialogInferenceLoading dialogLoadingInfer;
        private YoloPracticePresenter yoloPracticePresenter;
        private YoloTutorialPresenter yoloTutorialPresenter;

        private readonly IMainView mainView;

        private BlocklyModel blocklyModel;

        public event EventHandler HomeButtonClicked;
        public event EventHandler<BlockEventArgs> AddBlockButtonClicked;
        public event EventHandler AddBlockButtonDoubleClicked;
        private JsBridge jsBridge;

        private bool isInferPanelVisible = false;
        private int inferPanelWidth = 420;
        private bool isMemoPanelVisible = false;

        private double currentThreshold = 0.5; // threshold 기본값 0.5
        private string selectedImagePath = string.Empty; // 추론 이미지 경로를 저장할 변수
        private string currentImagePath = string.Empty; // 현재 표시 중인 이미지 경로

		private int undoCount = 0; // 뒤로가기 카운트
		private int blockCount = 0; // 블럭 개수

		private string errorMessage = "";
		private string missingType = "";
		private string errorType = "";

        private MemoPresenter memoPresenter;

		private CancellationTokenSource _toastCancellationSource;

        private int currentZoomLevel = 80; // 현재 확대/축소 레벨 (기본값 60%)
        private readonly int[] zoomLevels = { 0, 20, 40, 60, 80, 100, 120, 140, 160, 180, 200 }; // 가능한 확대/축소 레벨

        private PythonService.InferenceResult _result;

        public event EventHandler RunButtonClicked;

		private UcPracticeBlockList ucPracticeBlockList;

		public UcPracticeBlockCode(IMainView view)
        {
            InitializeComponent();

            yoloPracticePresenter = new YoloPracticePresenter(this);
            yoloTutorialPresenter = new YoloTutorialPresenter(this);
            
            // RunButtonClicked 이벤트를 yoloTutorialPresenter에서 해제
            yoloTutorialPresenter.UnsubscribeFromRunButtonClicked(this);

            ucShowDialogPresenter = new UcShowDialogPresenter(this);

            memoPresenter = new MemoPresenter();

            tboxMemo.TextChanged += tboxMemo_TextChanged;

            pleaseControlThreshold.Visible = false;

            // 홈페이지 이동
            ibtnHome.Click += (s, e) =>
            {
				var dialog = new DialogHomeFromTrain();
				dialog.ShowDialog(this);
		    };

            ibtnHome.BackColor = Color.Transparent;
            ibtnInfer.BackColor = Color.Transparent;
            ibtnMemo.BackColor = Color.Transparent;
            cAlertPanel.BackColor = Color.Transparent;
            ButtonUtils.SetTransparentStyle(btnCopy);

            // 초기에는 숨기길 패널들
            pSideInfer.Visible = false;
            ibtnCloseInfer.Visible = false;
            pMemo.Visible = false;
            cAlertPanel.Visible = false;  // 복사 알림 패널도 초기에 숨김
            btnSelectInferImage.Visible = false;

            // 새 이미지 불러오기 버튼 설정
            btnSelectInferImage.Size = new Size(494, 278);  // pInferAccuracy와 동일한 크기
            pboxInferAccuracy.Controls.Add(btnSelectInferImage);
            btnSelectInferImage.Location = new Point(0, 0);
            btnSelectInferImage.Enabled = true;
            btnSelectInferImage.Cursor = Cursors.Hand;

            pboxInferAccuracy.MouseEnter += (s, e) =>
            {
                if (pSideInfer.Visible)
                {
                    btnSelectInferImage.Visible = true;
                    btnSelectInferImage.BackgroundImage = Properties.Resources.btn_selectinferimage_hover;
                }
            };

            pboxInferAccuracy.MouseLeave += (s, e) =>
            {
                if (!btnSelectInferImage.ClientRectangle.Contains(btnSelectInferImage.PointToClient(Control.MousePosition)))
                {
                    btnSelectInferImage.Visible = false;
                    btnSelectInferImage.BackgroundImage = Properties.Resources.btn_selectinferimage;
                }
            };

            // 버튼에도 MouseEnter/Leave 이벤트 추가
            btnSelectInferImage.MouseEnter += (s, e) =>
            {
                btnSelectInferImage.Visible = true;
                btnSelectInferImage.BackgroundImage = Properties.Resources.btn_selectinferimage_hover;
            };

            btnSelectInferImage.MouseLeave += (s, e) =>
            {
                if (!pboxInferAccuracy.ClientRectangle.Contains(pboxInferAccuracy.PointToClient(Control.MousePosition)))
                {
                    btnSelectInferImage.Visible = false;
                    btnSelectInferImage.BackgroundImage = Properties.Resources.btn_selectinferimage;
                }
            };

            MemoUtils.ApplyStyle(tboxMemo);
            ScrollUtils.AdjustPanelScroll(pSideInfer);



            //ToolTipUtils.CustomToolTip(pboxGraphe, "자세히 보려면 클릭하세요.");
            ToolTipUtils.CustomToolTip(btnInfoThreshold,
              "AI의 분류 기준입니다. 예측 결과가 이 값보다 높으면 '맞다(1)'고 판단하고, 낮으면 '아니다(0)'로 처리합니다.");

            ToolTipUtils.CustomToolTip(btnInfoGraph,
              "AI 모델의 성능을 한눈에 확인할 수 있는 그래프입니다. 정확도, 재현율 등의 성능 지표가 포함되어 있습니다.");
            ToolTipUtils.CustomToolTip(btnSelectInferImage, "추론에 사용할 이미지를 가져오려면 클릭하세요.");

            ButtonUtils.SetupButton(btnRunModel, "btnRunModel_clicked", "btn_run_model");
            ButtonUtils.SetupButton(btnNextBlock, "btn_next_block_clicked", "btn_next_block1");
            ButtonUtils.SetupButton(btnPreBlock, "btn_pre_block_clicked", "btn_pre_block1");
            ButtonUtils.SetupButton(btnTrash, "btn_trash_clicked", "btn_trash_block");
            ButtonUtils.SetupButton(btnQuestionMemo, "btn_question_memo_clicked", "btn_question_memo");
            ButtonUtils.SetupButton(btnCloseMemo, "btn_close_25_clicked", "btn_close_25");
            ButtonUtils.SetupButton(btnCopy, "btn_copy_hover", "btn_copy");
            ButtonUtils.SetTransparentStyle(btnSelectInferImage);
            ButtonUtils.SetTransparentStyle(btnInfoGraph);
            ButtonUtils.SetTransparentStyle(btnInfoThreshold);
            pboxInferAccuracy.Image = null;


            blockCount = 0; // 블럭 개수 초기화
			undoCount = 0;
			btnNextBlock.Visible = false; // 처음에는 보이지 않게 설정
			btnPreBlock.Visible = false; // 처음에는 보이지 않게 설정
            // 정언이가 선언
            //생성자---------------
            blocklyPresenter = new BlocklyPresenter(this);
            this.mainView = view;
            blocklyModel = BlocklyModel.Instance;
            InitializeWebView2();

            btnRunModel.BackColor = Color.Transparent;
            btnRunModel.PressedColor = Color.Transparent;
            btnRunModel.CheckedState.FillColor = Color.Transparent;
            btnRunModel.DisabledState.FillColor = Color.Transparent;
            btnRunModel.HoverState.FillColor = Color.Transparent;
            // btnRunModel 마우스 입력 될 때
            btnRunModel.MouseEnter += (s, e) =>
            {
                btnRunModel.BackColor = Color.Transparent;
                btnRunModel.BackgroundImage = Properties.Resources.btnRunModel_clicked;
            };
            // btnRunModel 마우스 떠날때
            btnRunModel.MouseLeave += (s, e) =>
            {
                btnRunModel.BackgroundImage = Properties.Resources.btn_run_model;
            };
            // 스크롤바 설정-------------------
            ucPracticeBlockList = new UcPracticeBlockList(this, AddBlockButtonClicked);
            pSelectBlock.Controls.Add(ucPracticeBlockList);
            pSelectBlock.AutoScroll = false;
            ucPracticeBlockList.AutoScroll = false;
            pSelectBlockvScrollBar.Scroll += (s, e) =>
            {
                if (!pSelectBlockvScrollBar.Visible) return; // ❗ 스크롤바 안 보이면 무시

                ucPracticeBlockList.content.Top = -pSelectBlockvScrollBar.Value;
            };
            pSelectBlockvScrollBar.Maximum = ucPracticeBlockList.content.Height - pSelectBlockvScrollBar.Height;
            ucPracticeBlockList.SizeChanged += (s, e) =>
            {
                int contentHeight = ucPracticeBlockList.content.Height;
                int viewportHeight = pSelectBlock.Size.Height;

                int newMax = contentHeight - viewportHeight;
                if (newMax <= 0)
                {
                    pSelectBlockvScrollBar.Visible = false;
                    pSelectBlockvScrollBar.Maximum = 0;
                    pSelectBlockvScrollBar.Value = 0;
                    ucPracticeBlockList.content.Top = 0;
                }
                else
                {
                    pSelectBlockvScrollBar.Visible = true;
                    pSelectBlockvScrollBar.Maximum = newMax;
                }
            };
            pSelectBlock.MouseEnter += (s, e) => pSelectBlock.Focus();
            // 마우스 휠 이벤트 수동 처리
            pSelectBlock.MouseWheel += (s, e) =>
            {
                if (!pSelectBlockvScrollBar.Visible) return; // ❗ 스크롤 안 보이면 스킵

                int newValue = pSelectBlockvScrollBar.Value - e.Delta / 5; // 120 → 한 칸, 반전 여부 조절 가능
                newValue = Math.Max(pSelectBlockvScrollBar.Minimum, Math.Min(pSelectBlockvScrollBar.Maximum, newValue));
                pSelectBlockvScrollBar.Value = newValue;
            };

            btnCopy.Click += (s, e) =>
            {
                try
                {
                    // BlocklyModel에서 전체 코드 가져오기
                    string codeToCopy = blocklyModel.blockAllCode;

                    if (!string.IsNullOrEmpty(codeToCopy))
                    {
                        // 클립보드에 코드 복사
                        Clipboard.SetText(codeToCopy);
                        Console.WriteLine("[DEBUG] UcPracticeBlockCode: 코드가 클립보드에 복사됨");
                    }
                    else
                    {
                        Console.WriteLine("[WARNING] UcPracticeBlockCode: 복사할 코드가 없음");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] UcPracticeBlockCode: 코드 복사 중 오류 발생 - {ex.Message}");
                }
            };

            // 코드 확대/축소 버튼 및 퍼센트 표시 컨트롤 연결 (튜토리얼과 동일하게)
            guna2ImageButton2.Click += (s, e) =>
            {
                try
                {
                    int currentIndex = Array.IndexOf(zoomLevels, currentZoomLevel);
                    if (currentIndex < zoomLevels.Length - 1)
                    {
                        currentZoomLevel = zoomLevels[currentIndex + 1];
                        UpdateCodeZoom();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] UcPracticeBlockCode: 확대 중 오류 발생 - {ex.Message}");
                }
            };

            btnMinus.Click += (s, e) =>
            {
                try
                {
                    int currentIndex = Array.IndexOf(zoomLevels, currentZoomLevel);
                    if (currentIndex > 0)
                    {
                        currentZoomLevel = zoomLevels[currentIndex - 1];
                        UpdateCodeZoom();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] UcPracticeBlockCode: 축소 중 오류 발생 - {ex.Message}");
                }
            };

            // 초기 확대/축소 레벨 설정
            currentZoomLevel = 60;
            UpdateCodeZoom();

            // PercentUtils로 퍼센트 박스 스타일 일괄 적용
            PercentUtils.SetupPercentTextBox(guna2TextBox1, 0.5f, 0, 0);

            // mAlertPanel 초기에는 숨김
            mAlertPanel.Visible = false;
            // btnQuestionMemo 클릭 이벤트 핸들러 등록
            btnQuestionMemo.Click += btnQuestionMemo_Click;

            // 여기에 UcCode 추가
            try
            {
                if (ucCode２ != null)
                {
                    // BlocklyPresenter에 기존 ucCode２ 설정
                    blocklyPresenter.SetCodeView(ucCode２);
                    Console.WriteLine("[DEBUG] UcPracticeBlockCode: ICodeView 설정 완료");

                    // BlocklyModel 이벤트 구독 확인
                    blocklyModel.BlockAllCodeChanged += (code) =>
                    {
                        Console.WriteLine($"[DEBUG] UcPracticeBlockCode: BlockAllCodeChanged 이벤트 발생 - 코드 길이: {code?.Length ?? 0}");
                    };
                }
                else
                {
                    Console.WriteLine("[ERROR] UcPracticeBlockCode: ucCode２가 null입니다");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] UcPracticeBlockCode: ICodeView 설정 중 오류 - {ex.Message}");
            }

			///////////////////////////////////////////////////
			/// 재영 언니 여기야아아
			// 이미지 경로가 바뀌면 블록에서도 적용되게
			blocklyModel.ImgPathChanged += (newPath) => {
				// 웹뷰에 이미지 경로 전달
				//webViewblock.ExecuteScriptAsync($"imgPathChanged({{newPath}})");
                string escapedPath = JsonSerializer.Serialize(newPath);
                webViewblock.ExecuteScriptAsync($"imgPathChanged({escapedPath})");

                if (File.Exists(newPath))
                {
                    // 기존 이미지 정리
                    pboxInferAccuracy.Image?.Dispose();

                    // string 경로를 Image 객체로 변환
                    pboxInferAccuracy.Size = new Size(494,278);
                    pboxInferAccuracy.SizeMode = PictureBoxSizeMode.Zoom;
                    pboxInferAccuracy.Image = Image.FromFile(newPath);
                    pboxInferAccuracy.Visible = true;
                    pleaseControlThreshold.Visible = true;
                }
            };

			// threshold가 바뀌면 블록에서도 적용되게
			blocklyModel.AccuracyChanged += (newAccuracy) => {
				// 웹뷰에 threshold 전달
				webViewblock.ExecuteScriptAsync($"thresholdChanged({{newAccuracy}})");
                tboxThreshold.Text = newAccuracy.ToString();
                tbarThreshold.Value = (int)(newAccuracy * 100);
                pleaseControlThreshold.Visible = false;
            };

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var csvPath = Path.Combine(baseDir,
                @"..\..\SAI.Application\Python\runs\detect\train\results.csv");
            csvPath = Path.GetFullPath(csvPath);
            ShowTrainingChart(csvPath);
        }

		private void ShowpSIdeInfer()
        {
            pSideInfer.Visible = true;
            ibtnCloseInfer.Visible = true;
            isInferPanelVisible = true;
        }

        private void HidepSideInfer()
        {
            pSideInfer.Visible = false;
            ibtnCloseInfer.Visible = false;
            isInferPanelVisible = false;
        }

        private void SetupThresholdControls()
        {
            ThresholdUtilsPractice.Setup(
                tbarThreshold,
                tboxThreshold,
                (newValue) => 
                {
                    currentThreshold = newValue;

                    Console.WriteLine($"[LOG] SetupThresholdControls - selectedImagePath: {selectedImagePath}");
                    Console.WriteLine($"[LOG] SetupThresholdControls - currentThreshold: {currentThreshold}");

                    // 추론은 백그라운드에서 실행
                    // 이미지경로, threshold 값을 던져야 추론스크립트 실행 가능
                    Task.Run(async () =>
                    {
                        _result = await yoloTutorialPresenter.RunInferenceDirect(
                            selectedImagePath,
                            currentThreshold
                        );

                        Console.WriteLine($"[LOG] RunInferenceDirect 결과: success={_result.Success}, image={_result.ResultImage}, error={_result.Error}");
                        if (!string.IsNullOrEmpty(_result.ResultImage))
                        {
                            bool fileExists = System.IO.File.Exists(_result.ResultImage);
                            Console.WriteLine($"[LOG] ResultImage 파일 존재 여부: {fileExists}");
                        }
                        else
                        {
                            Console.WriteLine("[LOG] ResultImage가 비어있음");
                        }

                        // 결과는 UI 스레드로 전달
                        this.Invoke(new Action(() =>
                        {
                            ShowPracticeInferResultImage(_result);
                        }));
                    });
                },
                this

            );
        }

        private void ibtnHome_Click(object sender, EventArgs e)
        {
            HomeButtonClicked?.Invoke(this, EventArgs.Empty); // Presenter에게 알림
        }

        private void ibtnInfer_Click(object sender, EventArgs e)
        {
            if (!isInferPanelVisible)
            {
                ShowpSIdeInfer();
            }
            else
            {
                HidepSideInfer();
            }
        }
        private void ibtnCloseInfer_Click(object sender, EventArgs e)
        {
            HidepSideInfer();
        }

        private void ibtnMemo_Click(object sender, EventArgs e)
        {
            isMemoPanelVisible = !isMemoPanelVisible;
            pMemo.Visible = isMemoPanelVisible;
        }

        private void btnCloseMemo_Click(object sender, EventArgs e)
        {
            isMemoPanelVisible = !isMemoPanelVisible;
            pMemo.Visible = isMemoPanelVisible;
        }

        private void ibtnGoNotion_Click(object sender, EventArgs e)
        {
            string memo = memoPresenter.GetMemoText();
            double thresholdValue = tbarThreshold.Value/100.0;

            using (var dialog = new DialogNotion(memo, thresholdValue, _result.ResultImage, false))
            {
                dialog.ShowDialog();
            }
        }

        public void showDialog(Form dialog)
        {
            dialog.Owner = mainView as Form;
            dialog.ShowDialog();
        }

        // webview에 blockly tutorial html 붙이기
        private async void InitializeWebView2()
        {
            jsBridge = new JsBridge((message, type) =>
            {
                blocklyPresenter.HandleJsMessage(message, type, "practice");
            });

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string localPath = Path.GetFullPath(Path.Combine(baseDir, @"..\\..\\Blockly\\TrainBlockly.html"));
            string uri = new Uri(localPath).AbsoluteUri;

            webViewblock.WebMessageReceived += async (s, e) =>
            {
                try
                {
                    // 먼저 시도: 객체 기반 JSON 메시지 처리
                    var doc = JsonDocument.Parse(e.WebMessageAsJson);
                    var root = doc.RootElement;

                    if (root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("type", out var typeElem))
                    {
                        string type = typeElem.GetString();

                        switch (type)
                        {
                            case "openFile":
                                using (OpenFileDialog dialog = new OpenFileDialog())
                                {
                                    dialog.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp";
                                    dialog.Multiselect = false;
                                    string blockId = root.GetProperty("blockId").GetString(); // blockId를 가져옴
                                    if (dialog.ShowDialog() == DialogResult.OK)
                                    {
                                        string filePath = dialog.FileName.Replace("\\", "/");
                                        string escapedFilePath = JsonSerializer.Serialize(filePath);
                                        string escapedBlockId = JsonSerializer.Serialize(blockId); // 이건 위에서 받은 blockId

                                        string json = $@"{{
											""blockId"": {escapedBlockId},
											""filePath"": {escapedFilePath}
										}}";

                                        await webViewblock.ExecuteScriptAsync(
                                            $"window.dispatchEvent(new MessageEvent('message', {{ data: {json} }}));"
                                        );
                                    }
                                }
                                break;

                            case "blockAllCode":
                                string blockAllCode = root.GetProperty("code").GetString();
                                jsBridge.receiveMessageFromJs(blockAllCode, type);
                                break;

                            case "blockCode":
                                string blockCode = root.GetProperty("code").GetString();
                                jsBridge.receiveMessageFromJs(blockCode, type);
                                break;

                            case "blockDoubleClick":
                                string eventCode = root.GetProperty("code").GetString();
                                blocklyPresenter.OnAddBlockDoubleClicked(eventCode);
                                break;

							case "blockTypes":
								var jsonTypes = root.GetProperty("types");
								var blockTypes = JsonSerializer.Deserialize<List<BlockInfo>>(jsonTypes.GetRawText());
								blocklyPresenter.setBlockTypes(blockTypes);
								break;
							case "blockCount":
								var jsonCount = root.GetProperty("count").ToString();
								blockCount = int.Parse(jsonCount);
								break;
							case "blockCreated":
								var blockType = root.GetProperty("blockType").ToString();
								var newValue = root.GetProperty("allValues");
								var value = JsonSerializer.Deserialize<Dictionary<string, object>>(newValue.GetRawText());
								blocklyPresenter.setFieldValue(blockType, value);
								break;

							case "blockFieldUpdated":
								blockType = root.GetProperty("blockType").ToString();
								var allValues = root.GetProperty("allValues");
								value = JsonSerializer.Deserialize<Dictionary<string, object>>(allValues.GetRawText());
								blocklyPresenter.setFieldValue(blockType, value);
								break;
							case "blocksAllTypes":
								jsonTypes = root.GetProperty("types");
								blockTypes = JsonSerializer.Deserialize<List<BlockInfo>>(jsonTypes.GetRawText());
								blocklyPresenter.loadBlockEvent(blockTypes, ucPracticeBlockList);
								break;
							case "undoCount":
								var jsonUndoCount = root.GetProperty("cnt").ToString();
								var undoCnt = int.Parse(jsonUndoCount);
                                break;
						}
					}
				}
				catch (Exception ex)
				{
					MessageBox.Show($"WebView2 메시지 처리 오류: {ex.Message}");
				}
			};

            webViewblock.ZoomFactor = 0.5; // 줌 비율 설정

			await webViewblock.EnsureCoreWebView2Async();

			webViewblock.Source = new Uri(uri);
		}

		// JS 함수 호출 = 블럭 넣기
		public void addBlock(string blockType)
		{
			if(btnPreBlock.Visible == false)
			{
				btnPreBlock.Visible = true;
				btnNextBlock.Visible = false;
				undoCount = 0;
			}
			webViewblock.ExecuteScriptAsync($"addBlock('{blockType}')");
			webViewblock.ExecuteScriptAsync($"getblockCount()");
		}

        // JS 함수호출 = 하나의 블럭의 코드 가져오기
        public void getPythonCodeByType(string blockType)
        {
            webViewblock.ExecuteScriptAsync($"getPythonCodeByType('{blockType}')");
        }

        // blockly 웹뷰 확대 조절 함수
        private void webViewblock_ZoomFactorChanged(object sender, EventArgs e)
        {
            webViewblock.ZoomFactor = 0.5;
        }

		// JS 함수 호출 = 다시 실행하기
		private void btnNextBlock_Click(object sender, EventArgs e)
		{
			--undoCount;
			webViewblock.ExecuteScriptAsync($"redo()");

            if (undoCount == 0)
            {
                btnNextBlock.Visible = false;
                btnPreBlock.Visible = true;
            }
            else
            {
                btnNextBlock.Visible = true;
                btnPreBlock.Visible = true;
            }
        }

		// JS 함수 호출 = 되돌리기
		private void btnPreBlock_Click(object sender, EventArgs e)
		{
			++undoCount;
			webViewblock.ExecuteScriptAsync($"undo()");
			webViewblock.ExecuteScriptAsync($"getblockCount()");

			if (undoCount < 10 && undoCount > 0 && blockCount > 1) // <- 이거 왜 1이여야하지?
			{
				btnNextBlock.Visible = true;
				btnPreBlock.Visible = true;
			}
			else
			{
				btnNextBlock.Visible = true;
				btnPreBlock.Visible = false;
			}
		}

		// JS 함수 호출 - 블럭 모두 삭제
		private void btnTrash_Click(object sender, EventArgs e)
		{
            ucPracticeBlockList.isNotThereStart();
			webViewblock.ExecuteScriptAsync($"clear()");
		}

        private void ibtnAiFeedback_Click(object sender, EventArgs e)
        {
            string memo = memoPresenter.GetMemoText();
            double thresholdValue = tbarThreshold.Value / 100.0;

            using (var dialog = new DialogNotion(memo, thresholdValue, _result.ResultImage, false))
            {
                dialog.ShowDialog();
            }
        }

        private void pboxGraphe_Click(object sender, EventArgs e)
        {
            using (var dialog = new DialogModelPerformance())
            {
                dialog.ShowDialog();
            }
        }

		private bool checkBlockPosition(string blockType, int nowPosition)
		{
			if (nowPosition == 1)
			{
				if (blockType != "pipInstall")
				{
					blockErrorMessage("start");
					return false;
				}
			}
			else if (nowPosition == 2)
			{
				if(blockType != "loadModel" && blockType != "loadModelWithLayer")
				{
					blockErrorMessage("pipInstall");
					return false;
				}
			}
			else if (nowPosition == 3)
			{
				if (blockType != "loadDataset")
				{
					blockErrorMessage("loadModel");
					return false;
				}
			}
			else if (nowPosition == 4)
			{
				if (blockType != "machineLearning")
				{
					blockErrorMessage("loadDataset");
					return false;
				}
			}
			else if (nowPosition == 5)
			{
				if (blockType != "resultGraph")
				{
					blockErrorMessage("machineLearning");
					return false;
				}
			}
			else if (nowPosition == 6)
			{
				if (blockType != "imgPath")
				{
					blockErrorMessage("resultGraph");
					return false;
				}
			}
			else if (nowPosition == 7)
			{
				if (blockType != "modelInference")
				{
					blockErrorMessage("imgPath");
					return false;
				}
			}
			else if (nowPosition == 8)
			{
				if (blockType != "visualizeResult")
				{
					blockErrorMessage("modelInference");
					return false;
				}
			}

			return true;
		}

		private void blockErrorMessage(string blockType)
		{
			switch (blockType)
			{
				case "start":
					errorType = "블록 배치 오류";
					missingType = "MISSING \"패키지 설치\"";
					errorMessage = "\"패키지 설치\" 블록이 필요합니다. 아래 순서에 맞게 배치해주세요.\n";
					errorMessage += "[시작] - [패키지 설치]";
					break;
				case "pipInstall":
					errorType = "블록 배치 오류";
					missingType = "MISSING \"모델 불러오기\"";
					errorMessage = "\"모델 불러오기\" 블록이 필요합니다. 아래 순서에 맞게 배치해주세요.\n";
					errorMessage += "[패키지 설치] - [모델 불러오기]";
					break;
				case "loadModel":
					errorType = "블록 배치 오류";
					missingType = "MISSING \"데이터 불러오기\"";
					errorMessage = "\"데이터 불러오기\" 블록이 필요합니다. 아래 순서에 맞게 배치해주세요.\n";
					errorMessage += "[모델 불러오기] - [데이터 불러오기]";
					break;
				case "loadModelWithLayer":
					errorType = "블록 배치 오류";
					missingType = "MISSING \"데이터 불러오기\"";
					errorMessage = "\"데이터 불러오기\" 블록이 필요합니다. 아래 순서에 맞게 배치해주세요.\n";
					errorMessage += "[모델 불러오기] - [데이터 불러오기]";
					break;
				case "loadDataset":
					errorType = "블록 배치 오류";
					missingType = "MISSING \"모델 학습하기\"";
					errorMessage = "\"모델 학습하기\" 블록이 필요합니다. 아래 순서에 맞게 배치해주세요.\n";
					errorMessage += "[데이터 불러오기] - [모델 학습하기]";
					break;
				case "machineLearning":
					errorType = "블록 배치 오류";
					missingType = "MISSING \"학습 결과 그래프 출력하기\"";
					errorMessage = "\"학습 결과 그래프 출력하기\" 블록이 필요합니다. 아래 순서에 맞게 배치해주세요.\n";
					errorMessage += "[모델 학습하기] - [학습 결과 그래프 출력하기]";
					break;
				case "resultGraph":
					errorType = "블록 배치 오류";
					missingType = "MISSING \"이미지 불러오기\"";
					errorMessage = "\"이미지 불러오기\" 블록이 필요합니다. 아래 순서에 맞게 배치해주세요.\n";
					errorMessage += "[학습 결과 그래프 출력하기] - [이미지 불러오기]";
					break;
				case "imgPath":
					errorType = "블록 배치 오류";
					missingType = "MISSING \"추론 실행하기\"";
					errorMessage = "\"추론 실행하기\" 블록이 필요합니다. 아래 순서에 맞게 배치해주세요.\n";
					errorMessage += "[이미지 불러오기] - [추론 실행하기]";
					break;
				case "modelInference":
					errorType = "블록 배치 오류";
					missingType = "MISSING \"결과 시각화하기\"";
					errorMessage = "\"결과 시각화하기\" 블록이 필요합니다. 아래 순서에 맞게 배치해주세요.\n";
					errorMessage += "[추론 실행하기] - [결과 시각화하기]";
					break;
				case "layer":
					errorType = "블록 배치 오류";
					missingType = "MISSING \"모델 불러오기\"";
					errorMessage = "\"레이어 수정 가능한 모델 불러오기\" 블록이 필요합니다. 모델 불러오기 블록 안에 넣어주세요.\n";
					errorMessage += "[패키지 설치] - [레이어 수정 가능한 모델 불러오기]";
					break;
				case "overBlock":
					errorType = "블록 개수 오류";
					missingType = "불필요한 블럭 존재";
					errorMessage = "블럭이 필요 이상으로 너무 많습니다.\n";
					errorMessage += "필요한 블럭만 놓아주세요.";
					break;
			}
		}

		// 블록 에러 처리이이
		public bool isBlockError()
		{
            // start 블럭 밑에 붙어있는 블럭들의 순서를 판단
            for (int i = 0; i < blocklyModel.blockTypes.Count; i++)
            {
                BlockInfo blockType = blocklyModel.blockTypes[i];

                if (!checkBlockPosition(blockType.type, i))
                {
                    return true;
                }

                if (blockType.type == "loadModelWithLayer")
                {
                    if(blockType.children == null || blockType.children.Count == 0)
                    {
						errorType = "블록 배치 오류";
						missingType = "MISSING \"레이어\"";
						errorMessage = "\"레이어 수정하여 모델 불러오기\" 블록의 필수 자식 블록인 \"레이어\" 블록이 없습니다.\n";
						errorMessage += "\"레이어\" 블록을 넣어주세요.";
						return true;
                    }
                    else
                    {
                        for (int j = 0; j < blockType.children.Count; j++)
                        {
                            BlockInfo childBlock = blockType.children[j];
                            if (childBlock.type != "layer")
                            {
                                errorType = "블록 배치 오류";
                                missingType = "MISSING \"레이어\"";
                                errorMessage = "필수 자식 블록인 \"레이어\" 블록이 외 다른 블럭이 존재합니다.\n";
                                errorMessage += "\"레이어\" 블록만 넣어주세요.";
                                return true;
                            }
                        }

                        if(blockType.children.Count > 1)
                        {
						    errorType = "블록 개수 오류";
						    missingType = "레이어 과다";
						    errorMessage = "\"레이어\" 수정은 현재 한 번만 가능합니다. 블럭 한 개만 넣어주세요.\n";
						    errorMessage += "\"레이어\" 블록을 한 개만 넣어주세요.";
						    return true;
					    }
                    }
				}
			    else if (blockType.type == "imgPath")
				{
					if (string.IsNullOrEmpty(blocklyModel.imgPath))
					{
						errorType = "파라미터 오류";
						missingType = "MISSING 파라미터 \"이미지 파일\"";
						errorMessage = "\"이미지 불러오기\"블록의 필수 파라미터인 \"이미지 파일\"이 없습니다.\n";
						errorMessage += "\"파일 선택\"버튼을 눌러 이미지를 선택해주세요.";
						return true;
					}
				}
			}

            // 만약 count가 9개보다 적으면 마지막 블럭을 오류라고 처리.
            if (blocklyModel.blockTypes.Count < 9)
            {
                blockErrorMessage(blocklyModel.blockTypes[blocklyModel.blockTypes.Count - 1].type);
                return true;
            }
            else if(blocklyModel.blockTypes.Count > 9) // 9개 보다 블럭이 많으면
            {
				blockErrorMessage("overBlock");
				return true;
			}

			return false;
		}

		// 실행 버튼 클릭 이벤트
		private void btnRunModel_Click(object sender, EventArgs e)
		{
			if (blocklyModel.blockTypes != null)
			{
				if (!isBlockError()) // 순서가 맞을 때
				{
					// 생성한 모델 삭제
					string baseDir = AppDomain.CurrentDomain.BaseDirectory;
					string modelPath = Path.GetFullPath(Path.Combine(baseDir, @"..\\..\SAI.Application\\Python\\runs\\detect\\train\\weights\\best.pt"));

					var mainModel = MainModel.Instance;

                    btnRunModel.Enabled = false;

                    if (!File.Exists(modelPath) || mainModel.DontShowDeleteModelDialog)
					{
						runModel(sender, e);
					}
					else
					{
						var dialog = new DialogDeleteModel(runModel);
						dialog.ShowDialog(this);
					}
				}
				else
				{
					ShowToastMessage(errorType, missingType, errorMessage);
				}
			}
			else
			{
				errorType = "블록 배치 오류";
				missingType = "MISSING \"시작\"";
				errorMessage = "\"시작\" 블록이 맨 앞에 있어야 합니다.\n";
				errorMessage += "시작 블록에 다른 블록들을 연결해주세요.\n";
				ShowToastMessage(errorType, missingType, errorMessage);
			}
		}

		public void runModel(object sender, EventArgs e)
		{
			// 파이썬 코드 실행
			RunButtonClicked?.Invoke(sender, e);
		}

		private async void ShowToastMessage(string errorType, string missingType, string errorMessage)
		{
			// 이전 토스트 메시지가 있다면 취소
			_toastCancellationSource?.Cancel();
			_toastCancellationSource = new CancellationTokenSource();
			var token = _toastCancellationSource.Token;

			try
			{
				pErrorToast.Visible = true;
				pErrorToast.FillColor = Color.FromArgb(0, pErrorToast.FillColor);
				lbErrorType.Text = errorType;
				lbMissingType.Text = missingType;
				lbErrorMessage.Text = errorMessage;

				// 2초 대기 (취소 가능)
				await Task.Delay(5000, token);
				pErrorToast.Visible = false;
			}
			catch (OperationCanceledException)
			{
				// 토스트가 취소된 경우 아무것도 하지 않음
			}
			finally
			{
				_toastCancellationSource?.Dispose();
				_toastCancellationSource = null;
			}
		}

        private void UcPracticeBlockCode_Load(object sender, EventArgs e)
        {
            // 초기에는 숨기길 패널들
            pSideInfer.Visible = false;
            ibtnCloseInfer.Visible = false;
            pMemo.Visible = false;
            cAlertPanel.Visible = false;  // 복사 알림 패널도 초기에 숨김

            SetupThresholdControls();
            MemoUtils.ApplyStyle(tboxMemo);
        }

        /// <summary>
        /// Run 버튼을 다시 활성화하는 메서드 (연습 학습 취소 시 호출)
        /// </summary>
        public void EnableRunButton()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(EnableRunButton));
                return;
            }
            
            btnRunModel.Enabled = true;
            btnRunModel.BackgroundImage = Properties.Resources.btn_run_model;
            Console.WriteLine("[DEBUG] UcPracticeBlockCode: Run 버튼이 다시 활성화되었습니다.");
        }

        private void ucCode1_Load(object sender, EventArgs e)
        {
            // ucCode２ 로드 이벤트 처리
        }

        private void UpdateCodeZoom()
        {
            try
            {
                if (ucCode２ != null)
                {
                    // Scintilla 에디터의 폰트 크기 업데이트
                    ucCode２.UpdateFontSize(currentZoomLevel);
                    // 확대/축소 레벨 표시 업데이트
                    guna2TextBox1.Text = $"{currentZoomLevel}%";
                    Console.WriteLine($"[DEBUG] UcPracticeBlockCode: 코드 확대/축소 레벨 변경 - {currentZoomLevel}%");
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"[ERROR] UcPracticeBlockCode: 확대/축소 레벨 업데이트 중 오류 발생 - {ex.Message}");
            }
        }
                    
                    
        // 추론 이미지 불러오기
        private void btnSelectInferImage_Click(object sender, EventArgs e)
        {
            try
            {
                using (OpenFileDialog openFileDialog = new OpenFileDialog())
                {
                    openFileDialog.Filter = "이미지 파일|*.jpg;*.jpeg;*.png;*.bmp;*.gif";
                    openFileDialog.Title = "이미지 파일 선택";

                    if (openFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        string absolutePath = openFileDialog.FileName;
                        selectedImagePath = absolutePath;
                        //currentImagePath = absolutePath;
                        blocklyModel.imgPath = selectedImagePath;

                        // 1. 현재 스크롤 위치 저장
                        //var scrollPos = pSideInfer.AutoScrollPosition;

                        // UI 표시용 이미지
                        using (var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read))
                        {
                            var originalImage = System.Drawing.Image.FromStream(stream);
                            pboxInferAccuracy.Size = new Size(494, 278);
                            pboxInferAccuracy.SizeMode = PictureBoxSizeMode.Zoom;
                            pboxInferAccuracy.Image = originalImage;
                            pboxInferAccuracy.Visible = true;
                        }

                        btnSelectInferImage.Visible = false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"이미지 로드 중 오류가 발생했습니다: {ex.Message}", "오류",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void ShowDialogInferenceLoading()
        {
            if (dialogLoadingInfer == null || dialogLoadingInfer.IsDisposed)
            {
                dialogLoadingInfer = new DialogInferenceLoading();
                dialogLoadingInfer.Show();  // 비동기적으로 띄움
            }
        }


        // DialogInferenceLoading 닫고 pboxInferAccuracy에 추론 결과 이미지 띄우는 함수
            //var practiceView = new UcPracticeBlockCode(mainView);
            //practiceView.ShowPracticeInferResultImage(resultImage); 사용하시면 됩니다.
        public void ShowPracticeInferResultImage(PythonService.InferenceResult result)
        {
            btnRunModel.Enabled = true;

            if (InvokeRequired)
            {
                Invoke(new Action(() => ShowPracticeInferResultImage(result)));
                return;
            }

            dialogLoadingInfer?.Close();
            dialogLoadingInfer = null;

            if (result.Success)
            {
                if (File.Exists(result.ResultImage))
                {
                    try
                    {
                        // 결과 이미지 경로 저장
                        currentImagePath = result.ResultImage;
                        _result = result;
                        
                        // 파일 이름에 한글이 포함된 경우 Stream을 통해 로드하여 문제 해결
                        using (var stream = new FileStream(result.ResultImage, FileMode.Open, FileAccess.Read))
                        {
                            var image = System.Drawing.Image.FromStream(stream);

                            // ✅ 직접 PictureBox에 표시
                            pboxInferAccuracy.Size = new Size(494, 278);
                            pboxInferAccuracy.SizeMode = PictureBoxSizeMode.Zoom;
                            pboxInferAccuracy.Image = image;
                            pboxInferAccuracy.Visible = true;
                            btnSelectInferImage.Visible = false;

                            if (!isInferPanelVisible)
                            {
                                ShowpSIdeInfer();
                                Console.WriteLine("[DEBUG] 추론 패널 표시됨");
                            }


                            // 이미지 클릭 시 원본 이미지를 열 수 있다는 정보 표시
                            ToolTip toolTip = new ToolTip();
                            toolTip.SetToolTip(pboxInferAccuracy, "이미지를 클릭하여 원본 크기로 보기");
                            
                            // 원본 파일명 정보 표시 (필요한 경우)
                            if (!string.IsNullOrEmpty(result.OriginalName))
                            {
                                Console.WriteLine($"[INFO] 원본 이미지 파일명: {result.OriginalName}");
                                // 여기에 원본 파일명을 표시하는 코드 추가 가능
                                // 예: lblOriginalFilename.Text = result.OriginalName;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[ERROR] 이미지 로드 실패: " + ex.Message);
                        MessageBox.Show($"이미지를 로드하는 도중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    MessageBox.Show($"결과 이미지 파일을 찾을 수 없습니다: {result.ResultImage}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                //btnSelectInferImage.Visible = true;
                //pboxInferAccuracy.Visible = false;
                var dialog = new DialogErrorInference();
                dialog.SetErrorMessage(result.Error);
                dialog.ShowDialog(this);

            }
        }

        private void tboxMemo_TextChanged(object sender, EventArgs e)
        {
            // MemoPresenter를 통해 텍스트 변경 사항을 모델에 저장
            if (memoPresenter != null)
            {
                memoPresenter.SaveMemoText(tboxMemo.Text);
            }
        }

        private void btnQuestionMemo_Click(object sender, EventArgs e)
        {
            // mAlertPanel을 보이게 설정
            mAlertPanel.Visible = true;

            // 2초 후에 mAlertPanel을 숨기는 타이머 설정
            Timer timer = new Timer();
            timer.Interval = 2000; // 2초
            timer.Tick += (s, args) =>
            {
                mAlertPanel.Visible = false;
                timer.Stop();
                timer.Dispose();
            };
            timer.Start();
        }

        private void btnCopy_Click(object sender, EventArgs e)
        {
            try
            {
                // BlocklyModel에서 전체 코드 가져오기
                string codeToCopy = blocklyModel.blockAllCode;
                
                if (!string.IsNullOrEmpty(codeToCopy))
                {
                    // 클립보드에 코드 복사
                    Clipboard.SetText(codeToCopy);
                    Console.WriteLine("[DEBUG] UcPracticeBlockCode: 코드가 클립보드에 복사됨");
                }
                else
                {
                    Console.WriteLine("[WARNING] UcPracticeBlockCode: 복사할 코드가 없음");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] UcPracticeBlockCode: 코드 복사 중 오류 발생 - {ex.Message}");
            }

            // cAlertPanel을 보이게 설정
            cAlertPanel.Visible = true;

            // 1초 후에 cAlertPanel을 숨기는 타이머 설정
            Timer timer = new Timer();
            timer.Interval = 1000; // 1초
            timer.Tick += (s, args) =>
            {
                cAlertPanel.Visible = false;
                timer.Stop();
                timer.Dispose();
            };
            timer.Start();
        }

        private async void btnSaveModel_Click(object sender, EventArgs e)
        {
            string modelFileName = "best.pt";

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            //모델 경로 다시 물어보기
            string _modelPath = Path.GetFullPath(Path.Combine(baseDir, @"..\\..\\SAI.Application\\Python\\runs\\detect\\train\\weights\\best.pt"));

            if (!File.Exists(_modelPath))
            {
                MessageBox.Show(
                    $"모델 파일을 찾을 수 없습니다.\n{_modelPath}",
                    "오류",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "모델을 복사할 폴더를 선택하세요.";
                folderDialog.ShowNewFolderButton = true;

                if (folderDialog.ShowDialog() == DialogResult.OK)
                {
                    string destPath = Path.Combine(folderDialog.SelectedPath, modelFileName);

                    // 비동기 복사 (UI 멈춤 방지)
                    await CopyModelAsync(_modelPath, destPath);

                    MessageBox.Show(
                        $"모델이 복사되었습니다.\n{destPath}",
                        "완료",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);


                    throw new NotImplementedException();
                }
            }
        }

        public void ClearLog()
        {
            // Debug 출력에서는 Clear() 대신 구분선을 출력하여 로그를 구분
            Debug.WriteLine("\n" + new string('-', 50) + "\n");
        }

        public void SetLogVisible(bool visible)
        {
        }
                    
                
        public void ShowErrorMessage(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new MethodInvoker(() => ShowErrorMessage(message)));
            }
            else
            {
                MessageBox.Show(message, "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Task CopyModelAsync(string source, string destination)
        {
            return Task.Run(() =>
            {
                // 존재할 경우 덮어쓰기(true)
                File.Copy(source, destination, overwrite: true);
            });
        }

        public void AppendLog(string text)
        {
            Debug.WriteLine($"[YOLO Tutorial] {text}");
        }

         public void ShowTrainingChart(string csvPath)
        {
            try
            {
                if (!File.Exists(csvPath))
                {
                    //ShowErrorMessage($"CSV 파일을 찾을 수 없습니다.\n{csvPath}");
                    return;
                }

                /* ① CSV → LogCsvModel 채우기 */
                var model = LogCsvModel.instance;
                new LogCsvPresenter(null).LoadCsv(csvPath);   // 데이터만 채우는 전용 메서드(아래 4-b) 참고)

                /* ② 차트 갱신 */
                ucCsvChart1.SetData();      // 내부에서 model 값을 읽어 그림
                ucCsvChart1.Visible = true; // 필요하면 처음엔 Visible=false 로 해두고 여기서 켜기
            }
            catch (Exception ex)
            {
                //ShowErrorMessage($"차트 로드 중 오류가 발생했습니다: {ex.Message}");
            }
        }

        public void ShowTutorialTrainingChart(string csvPath)
        {
            return;
        }


        private void pErrorCloseBtn_Click(object sender, EventArgs e)
        {
            pErrorToast.Visible = false;
        }
    }
}
